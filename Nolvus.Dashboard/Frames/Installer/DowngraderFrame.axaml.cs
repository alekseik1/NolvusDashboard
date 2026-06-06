using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Nolvus.Core.Events;
using Nolvus.Core.Frames;
using Nolvus.Core.Interfaces;
using Nolvus.Core.Services;
using Nolvus.Core.Enums;
using Nolvus.Dashboard.Controls;
using Nolvus.StockGame.Core;
using Vcc.Nolvus.Api.Installer.Library;
using Vcc.Nolvus.Api.Installer.Services;

namespace Nolvus.Dashboard.Frames.Installer
{
    public partial class DowngraderFrame : DashboardFrame
    {
        private readonly ObservableCollection<string> _output = new();

        public DowngraderFrame(IDashboard Dashboard, FrameParameters Params) : base(Dashboard, Params)
        {
            InitializeComponent();
            LstBxOutput.ItemsSource = _output;
        }

        protected override async Task OnLoadedAsync()
        {
            ServiceSingleton.Dashboard.Title("Nolvus Dashboard - [Stock Game Creation]");
            ServiceSingleton.Dashboard.Info("Stock Game Creation");

            TxtSkyrimDir.Text = ServiceSingleton.Game.GetSkyrimSEDirectory();

            if (!Parameters.IsEmpty && Parameters["Instance"] is INolvusInstance instance)
                TxtOutputDir.Text = instance.StockGame;

            BtnBrowseSkyrimDir.Click += BtnBrowseSkyrimDir_Click;
            BtnBrowseOutputDir.Click += BtnBrowseOutputDir_Click;
            BtnStart.Click += BtnStart_Click;

            var libXdelta = System.IO.Path.Combine(ServiceSingleton.Folders.LibDirectory, "xdelta3");
            bool xdeltaFound = System.IO.File.Exists(libXdelta)
                || Nolvus.Core.Utils.ExecutableResolver.FindExecutable("xdelta3") != null;

            if (!xdeltaFound)
            {
                BtnStart.IsEnabled = false;
                await AddItemToList("xdelta3 not found. Install it (e.g. apt install xdelta3) or place it at lib/xdelta3.");
            }
        }

        private async void BtnBrowseSkyrimDir_Click(object? sender, RoutedEventArgs e)
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner == null)
                return;

            var result = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Skyrim Directory",
                AllowMultiple = false
            });

            if (result.Count == 0)
                return;

            TxtSkyrimDir.Text = result[0].Path.LocalPath;
        }

        private async void BtnBrowseOutputDir_Click(object? sender, RoutedEventArgs e)
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner == null)
                return;

            var result = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Output Directory",
                AllowMultiple = false
            });

            if (result.Count == 0)
                return;

            var folderPath = result[0].Path.LocalPath;

            if (!ServiceSingleton.Files.IsDirectoryEmpty(folderPath))
            {
                NolvusMessageBox.Show(owner, "Invalid Output Directory", "The output directory is not empty! Please select an empty directory.", MessageBoxType.Error);
                return;
            }

            TxtOutputDir.Text = folderPath;
        }

        private async void BtnStart_Click(object? sender, RoutedEventArgs e)
        {
            var owner = TopLevel.GetTopLevel(this) as Window;

            if (string.IsNullOrWhiteSpace(TxtSkyrimDir.Text))
            {
                await NolvusMessageBox.Show(owner, "Validation Error", "Please select a Skyrim directory.", MessageBoxType.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(TxtOutputDir.Text))
            {
                await NolvusMessageBox.Show(owner, "Validation Error", "Please select an output directory.", MessageBoxType.Error);
                return;
            }

            if (TxtSkyrimDir.Text == TxtOutputDir.Text)
            {
                await NolvusMessageBox.Show(owner, "Validation Error", "Skyrim directory and output directory must be different.", MessageBoxType.Error);
                return;
            }

            if (!System.IO.Directory.Exists(TxtSkyrimDir.Text))
            {
                await NolvusMessageBox.Show(owner, "Validation Error", "Skyrim directory does not exist.", MessageBoxType.Error);
                return;
            }

            if (!ServiceSingleton.Files.IsDirectoryEmpty(TxtOutputDir.Text))
            {
                await NolvusMessageBox.Show(owner, "Validation Error", "Output directory is not empty.", MessageBoxType.Error);
                return;
            }

            BtnBrowseSkyrimDir.IsEnabled = false;
            BtnBrowseOutputDir.IsEnabled = false;
            BtnStart.IsEnabled = false;

            _output.Clear();

            try
            {
                var stockGameManager = new StockGameManager(
                    ServiceSingleton.Folders.DownloadDirectory,
                    ServiceSingleton.Folders.LibDirectory,
                    ServiceSingleton.Folders.PatchDirectory,
                    TxtSkyrimDir.Text,
                    TxtOutputDir.Text,
                    "English",
                    "EN",
                    await ApiManager.Service.Installer.GetLatestGamePackage(),
                    false);

                stockGameManager.OnDownload += StockGameManager_OnDownload;
                stockGameManager.OnExtract += StockGameManager_OnExtract;
                stockGameManager.OnItemProcessed += StockGameManager_OnItemProcessed;
                stockGameManager.OnStepProcessed += StockGameManager_OnStepProcessed;

                await stockGameManager.Load();
                if (ChkSkipHash.IsChecked != true)
                    await stockGameManager.CheckIntegrity();
                await stockGameManager.CopyGameFiles();
                await stockGameManager.PatchGameFiles();
                await AddItemToList("Stock Game creation completed successfully.");
                ServiceSingleton.Dashboard.ProgressCompleted();
            }
            catch (Exception ex)
            {
                await RollBack(TxtOutputDir.Text);

                if (ex is GameFileMissingException)
                {
                    await ServiceSingleton.Dashboard.Error(
                        "Error during game file checking",
                        "Skyrim Anniversary Edition is not installed",
                        ex.Message);
                }
                else if (ex is GameFileIntegrityException)
                {
                    await ServiceSingleton.Dashboard.Error("Error during game integrity checking", ex.Message);
                }
                else if (ex is GameFilePatchingException)
                {
                    await ServiceSingleton.Dashboard.Error("Error during game files patching", ex.Message);
                }
                else
                {
                    await ServiceSingleton.Dashboard.Error("Error during stock game creation", ex.Message, ex.StackTrace);
                }
            }
            finally
            {
                BtnBrowseSkyrimDir.IsEnabled = true;
                BtnBrowseOutputDir.IsEnabled = true;
                BtnStart.IsEnabled = true;
            }
        }

        private void StockGameManager_OnDownload(object? sender, DownloadProgress e)
        {
            ServiceSingleton.Dashboard.Status("Downloading file (" + e.ProgressPercentage + "%)...");
            ServiceSingleton.Dashboard.Progress(e.ProgressPercentage);
        }

        private void StockGameManager_OnExtract(object? sender, ExtractProgress e)
        {
            ServiceSingleton.Dashboard.Status("Extracting game meta (" + e.ProgressPercentage + "%)...");
            ServiceSingleton.Dashboard.Progress(e.ProgressPercentage);
        }

        private void StockGameManager_OnItemProcessed(object? sender, ItemProcessedEventArgs e)
        {
            double Percent = ((double)e.Value / (double)e.Total) * 100;
            Percent = Math.Round(Percent, 0);

            switch (e.Step)
            {
                case StockGameProcessStep.GameFileInfoLoading:
                    ServiceSingleton.Dashboard.Status(string.Format("Loading game files info for {0}...", e.ItemName));
                    ServiceSingleton.Dashboard.AdditionalInfo(string.Format("Loading game files info {0}", Percent));
                    ServiceSingleton.Dashboard.Progress(System.Convert.ToInt16(Percent));
                    break;
                case StockGameProcessStep.PatchingInfoLoading:
                    ServiceSingleton.Dashboard.Status(string.Format("Loading patching info for {0}...", e.ItemName));
                    ServiceSingleton.Dashboard.AdditionalInfo(string.Format("Loading patching info ({0}%)", Percent));
                    ServiceSingleton.Dashboard.Progress(System.Convert.ToInt16(Percent));
                    break;
                case StockGameProcessStep.GameFilesChecking:
                    ServiceSingleton.Dashboard.Status(string.Format("Checking game file {0}...", e.ItemName));
                    ServiceSingleton.Dashboard.AdditionalInfo(string.Format("Game files checking ({0}%)", Percent));
                    ServiceSingleton.Dashboard.Progress(System.Convert.ToInt16(Percent));
                    break;
                case StockGameProcessStep.GameFilesCopy:
                    ServiceSingleton.Dashboard.Status(string.Format("Copying game file {0}...", e.ItemName));
                    ServiceSingleton.Dashboard.AdditionalInfo(string.Format("Copying game files ({0}%)", Percent));
                    ServiceSingleton.Dashboard.Progress(System.Convert.ToInt16(Percent));
                    break;
                case StockGameProcessStep.GameFilesPatching:
                    ServiceSingleton.Dashboard.Status("Awaiting game file to patch...");
                    ServiceSingleton.Dashboard.AdditionalInfo(string.Format("Patching game files ({0}%)", Percent));
                    break;
                case StockGameProcessStep.PatchGameFile:
                    ServiceSingleton.Dashboard.Status(string.Format("Patching game files {0}...", e.ItemName));
                    ServiceSingleton.Dashboard.Progress(System.Convert.ToInt16(Percent));
                    break;
                case StockGameProcessStep.CheckPatchedGameFile:
                    ServiceSingleton.Dashboard.Status(string.Format("Checking patched game files {0}...", e.ItemName));
                    ServiceSingleton.Dashboard.Progress(System.Convert.ToInt16(Percent));
                    break;
            }
        }

        private void StockGameManager_OnStepProcessed(object? sender, StepProcessedEventArgs e)
        {
            _ = AddItemToList(e.Step);
        }

        public Task AddItemToList(string Item)
        {
            ServiceSingleton.Logger.Log(Item);

            return Dispatcher.UIThread.InvokeAsync(() =>
            {
                _output.Add(Item);

                if (_output.Count > 0)
                    LstBxOutput.ScrollIntoView(_output.Count - 1);
            }).GetTask();
        }

        private async Task RollBack(string outputDir)
        {
            await AddItemToList("Error detected, rolling back changes...");
            try
            {
                await Task.Run(() => ServiceSingleton.Files.RemoveDirectory(outputDir, true));
                await AddItemToList("Rollback complete.");
            }
            catch (Exception ex)
            {
                await AddItemToList("Rollback failed: " + ex.Message);
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ServiceSingleton.Dashboard.NoStatus();
                    ServiceSingleton.Dashboard.AdditionalInfo(string.Empty);
                    ServiceSingleton.Dashboard.ProgressCompleted();
                });
            }
        }
    }
}
