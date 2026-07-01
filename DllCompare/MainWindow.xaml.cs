using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DllCompare
{
    public sealed partial class MainWindow : Window
    {
        private static readonly SolidColorBrush AddedBrush = new(Microsoft.UI.Colors.ForestGreen);
        private static readonly SolidColorBrush RemovedBrush = new(Microsoft.UI.Colors.Firebrick);
        private static readonly SolidColorBrush UnchangedBrush = new(Microsoft.UI.Colors.DimGray);

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void BrowseLeftButton_Click(object sender, RoutedEventArgs e)
        {
            string? path = await PickDllAsync();
            if (path is not null)
            {
                await SetLeftPathAsync(path);
            }
        }

        private async void BrowseRightButton_Click(object sender, RoutedEventArgs e)
        {
            string? path = await PickDllAsync();
            if (path is not null)
            {
                await SetRightPathAsync(path);
            }
        }

        private async void CompareButton_Click(object sender, RoutedEventArgs e)
        {
            await CompareIfReadyAsync(forceMessage: true);
        }

        private void DllPathTextBox_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }

        private async void LeftPathTextBox_Drop(object sender, DragEventArgs e)
        {
            string? path = await GetDroppedDllPathAsync(e);
            if (path is not null)
            {
                await SetLeftPathAsync(path);
            }
        }

        private async void RightPathTextBox_Drop(object sender, DragEventArgs e)
        {
            string? path = await GetDroppedDllPathAsync(e);
            if (path is not null)
            {
                await SetRightPathAsync(path);
            }
        }

        private async Task SetLeftPathAsync(string path)
        {
            LeftPathTextBox.Text = path;
            await LoadSingleTreeAsync(path, isLeft: true);
            await CompareIfReadyAsync(forceMessage: false);
        }

        private async Task SetRightPathAsync(string path)
        {
            RightPathTextBox.Text = path;
            await LoadSingleTreeAsync(path, isLeft: false);
            await CompareIfReadyAsync(forceMessage: false);
        }

        private async Task LoadSingleTreeAsync(string path, bool isLeft)
        {
            try
            {
                AssemblyMetadataDocument document = await Task.Run(() => AssemblyMetadataComparer.Read(path));

                if (isLeft)
                {
                    LeftHeaderTextBlock.Text = $"Original - {document.Name}";
                    PopulateTree(LeftTreeView, document, new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), DifferenceKind.None);
                }
                else
                {
                    RightHeaderTextBlock.Text = $"Rebuilt - {document.Name}";
                    PopulateTree(RightTreeView, document, new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), DifferenceKind.None);
                }

                StatusTextBlock.Text = "Loaded metadata. Add the other DLL to compare.";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Load failed: {ex.Message}";
            }
        }

        private async Task CompareIfReadyAsync(bool forceMessage)
        {
            string leftPath = LeftPathTextBox.Text;
            string rightPath = RightPathTextBox.Text;

            if (string.IsNullOrWhiteSpace(leftPath) || string.IsNullOrWhiteSpace(rightPath))
            {
                if (forceMessage)
                {
                    StatusTextBlock.Text = "Select or drop both DLL files before comparing.";
                }

                return;
            }

            CompareButton.IsEnabled = false;
            StatusTextBlock.Text = "Comparing metadata...";

            try
            {
                AssemblyComparisonResult result = await Task.Run(() => AssemblyMetadataComparer.Compare(leftPath, rightPath));
                ApplyComparisonResult(result);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Compare failed: {ex.Message}";
            }
            finally
            {
                CompareButton.IsEnabled = true;
            }
        }

        private void ApplyComparisonResult(AssemblyComparisonResult result)
        {
            LeftHeaderTextBlock.Text = $"Original - {result.LeftName}";
            RightHeaderTextBlock.Text = $"Rebuilt - {result.RightName}";

            PopulateTree(
                LeftTreeView,
                result.LeftDocument,
                result.RemovedNamespaces.ToHashSet(StringComparer.Ordinal),
                result.RemovedTypes.ToHashSet(StringComparer.Ordinal),
                result.RemovedMethods.ToHashSet(StringComparer.Ordinal),
                DifferenceKind.Removed);
            PopulateTree(
                RightTreeView,
                result.RightDocument,
                result.AddedNamespaces.ToHashSet(StringComparer.Ordinal),
                result.AddedTypes.ToHashSet(StringComparer.Ordinal),
                result.AddedMethods.ToHashSet(StringComparer.Ordinal),
                DifferenceKind.Added);

            StatusTextBlock.Text = result.HasDifferences
                ? "Metadata shape differences found. Expand highlighted nodes to inspect details."
                : "No metadata shape differences found.";
            CountsTextBlock.Text =
                $"+ namespaces: {result.AddedNamespaces.Count}\n" +
                $"- namespaces: {result.RemovedNamespaces.Count}\n" +
                $"+ types     : {result.AddedTypes.Count}\n" +
                $"- types     : {result.RemovedTypes.Count}\n" +
                $"+ methods   : {result.AddedMethods.Count}\n" +
                $"- methods   : {result.RemovedMethods.Count}";
        }

        private static void PopulateTree(
            TreeView treeView,
            AssemblyMetadataDocument document,
            IReadOnlySet<string> changedNamespaces,
            IReadOnlySet<string> changedTypes,
            IReadOnlySet<string> changedMethods,
            DifferenceKind changedKind)
        {
            treeView.RootNodes.Clear();

            TreeViewNode root = new()
            {
                Content = CreateNodeContent(document.Name, DifferenceKind.None),
                IsExpanded = true
            };
            treeView.RootNodes.Add(root);

            foreach (NamespaceMetadataNode ns in document.Namespaces)
            {
                bool namespaceHasChangedChildren = ns.Types.Any(type =>
                    changedTypes.Contains(type.Key) || type.Methods.Any(method => changedMethods.Contains(method.Key)));
                DifferenceKind namespaceKind = changedNamespaces.Contains(ns.Key) || namespaceHasChangedChildren
                    ? changedKind
                    : DifferenceKind.None;
                TreeViewNode namespaceNode = new()
                {
                    Content = CreateNodeContent($"namespace {ns.DisplayName}", namespaceKind),
                    IsExpanded = namespaceKind != DifferenceKind.None
                };
                root.Children.Add(namespaceNode);

                foreach (TypeMetadataNode type in ns.Types)
                {
                    bool typeHasChangedMethods = type.Methods.Any(method => changedMethods.Contains(method.Key));
                    DifferenceKind typeKind = changedTypes.Contains(type.Key) || typeHasChangedMethods
                        ? changedKind
                        : DifferenceKind.None;
                    TreeViewNode typeNode = new()
                    {
                        Content = CreateNodeContent(type.DisplayName, typeKind),
                        IsExpanded = typeKind != DifferenceKind.None
                    };
                    namespaceNode.Children.Add(typeNode);

                    foreach (MethodMetadataNode method in type.Methods)
                    {
                        DifferenceKind methodKind = changedMethods.Contains(method.Key) ? changedKind : typeKind;
                        typeNode.Children.Add(new TreeViewNode
                        {
                            Content = CreateNodeContent(method.DisplayName, methodKind)
                        });
                    }
                }
            }
        }

        private static TextBlock CreateNodeContent(string text, DifferenceKind kind)
        {
            string prefix = kind switch
            {
                DifferenceKind.Added => "+ ",
                DifferenceKind.Removed => "- ",
                _ => "  "
            };

            return new TextBlock
            {
                Text = prefix + text,
                Foreground = kind switch
                {
                    DifferenceKind.Added => AddedBrush,
                    DifferenceKind.Removed => RemovedBrush,
                    _ => UnchangedBrush
                },
                FontFamily = new FontFamily("Consolas")
            };
        }

        private async Task<string?> PickDllAsync()
        {
            FileOpenPicker picker = new()
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add(".dll");

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            StorageFile? file = await picker.PickSingleFileAsync();
            return file?.Path;
        }

        private static async Task<string?> GetDroppedDllPathAsync(DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return null;
            }

            IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
            StorageFile? file = items.OfType<StorageFile>().FirstOrDefault(item =>
                string.Equals(Path.GetExtension(item.Path), ".dll", StringComparison.OrdinalIgnoreCase));
            return file?.Path;
        }

        private enum DifferenceKind
        {
            None,
            Added,
            Removed
        }
    }
}
