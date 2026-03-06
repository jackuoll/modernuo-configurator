using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ModernUOConfigurator;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModernUOConfigurator.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private Window? _window;
    private JsonObject? _serverConfigRoot;
    private Process? _runningProcess;
    private readonly StringBuilder _outputBuilder = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand), nameof(RunCommand), nameof(BuildAndRunCommand))]
    private string _rootPath = "";

    [ObservableProperty] private string _statusMessage = "Browse to your ModernUO root folder and click Load.";
    [ObservableProperty] private ObservableCollection<string> _dataDirectories = [];
    [ObservableProperty] private string? _selectedDataDir;
    [ObservableProperty] private List<ExpansionPreset> _expansionPresets = [];
    [ObservableProperty] private ExpansionPreset? _selectedPreset;
    [ObservableProperty] private string _currentExpansionName = "(not loaded)";
    [ObservableProperty] private List<SettingGroup> _settingGroups = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredSettingGroups))]
    private string _settingsSearch = "";

    partial void OnSettingGroupsChanged(List<SettingGroup> value) =>
        OnPropertyChanged(nameof(FilteredSettingGroups));

    public IEnumerable<SettingGroup> FilteredSettingGroups =>
        string.IsNullOrWhiteSpace(SettingsSearch)
            ? SettingGroups
            : SettingGroups.Where(g => g.Items.Any(i =>
                i.Key.Contains(SettingsSearch, StringComparison.OrdinalIgnoreCase)));

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand), nameof(RunCommand), nameof(BuildAndRunCommand), nameof(StopCommand), nameof(RestoreSaveCommand), nameof(BackupCurrentSavesCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _outputText = "";
    [ObservableProperty] private string _buildCommandText = "cmd /c publish.cmd";
    [ObservableProperty] private string _runCommandText = "ModernUO.exe";

    // ── Save Management ──────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<SaveEntry> _saveEntries = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreSaveCommand), nameof(UpdateSaveDescriptionCommand))]
    private SaveEntry? _selectedSaveEntry;

    [ObservableProperty] private string _selectedSaveDescription = "";

    partial void OnSelectedSaveEntryChanged(SaveEntry? value) =>
        SelectedSaveDescription = value?.Description ?? "";

    // Notification hook for the view to auto-scroll output
    public event Action? OutputAppended;

    private string DistributionPath => Path.Combine(RootPath, "Distribution");
    private string ModernUOJsonPath => Path.Combine(DistributionPath, "Configuration", "modernuo.json");
    private string ExpansionJsonPath => Path.Combine(DistributionPath, "Configuration", "expansion.json");
    private string ExpansionsDataPath => Path.Combine(DistributionPath, "Data", "expansions.json");
    private string SavesPath => Path.Combine(DistributionPath, "Saves");
    private string BackupsPath => Path.Combine(DistributionPath, "Backups");
    private string DescriptionsFilePath => Path.Combine(BackupsPath, "descriptions.json");

    private static readonly JsonSerializerOptions _writeOpts = new() { WriteIndented = true };

    public void Initialize(Window window)
    {
        _window = window;
        RootPath = AppSettings.LoadRootPath()
            ?? TryFindRootFolder()
            ?? "";
        if (!string.IsNullOrEmpty(RootPath))
            Load();
    }

    private static string? TryFindRootFolder()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            // Identify repo root by presence of ModernUO.sln or Distribution/Configuration/modernuo.json
            if (File.Exists(Path.Combine(dir, "ModernUO.sln")) ||
                File.Exists(Path.Combine(dir, "Distribution", "Configuration", "modernuo.json")))
                return dir;
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == null) break;
            dir = parent;
        }
        return null;
    }

    [RelayCommand]
    private async Task BrowseRoot()
    {
        if (_window == null) return;
        var result = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select ModernUO Root Folder (contains ModernUO.sln)"
        });
        if (result.Count > 0)
        {
            RootPath = result[0].Path.LocalPath;
            Load();
        }
    }

    [RelayCommand]
    private void Load()
    {
        try
        {
            if (!File.Exists(ModernUOJsonPath))
            {
                StatusMessage = $"modernuo.json not found. Expected: {ModernUOJsonPath}";
                return;
            }

            _serverConfigRoot = JsonNode.Parse(File.ReadAllText(ModernUOJsonPath))?.AsObject();
            if (_serverConfigRoot == null)
            {
                StatusMessage = "Failed to parse modernuo.json.";
                return;
            }

            DataDirectories.Clear();
            if (_serverConfigRoot["dataDirectories"]?.AsArray() is { } dataDirs)
                foreach (var d in dataDirs)
                    if (d?.GetValue<string>() is { } s)
                        DataDirectories.Add(s);

            LoadSettings();
            LoadExpansionPresets();

            if (File.Exists(ExpansionJsonPath))
            {
                var expNode = JsonNode.Parse(File.ReadAllText(ExpansionJsonPath));
                CurrentExpansionName = expNode?["Name"]?.GetValue<string>() ?? "(unknown)";
                var curId = expNode?["Id"]?.GetValue<int>() ?? -1;
                SelectedPreset = ExpansionPresets.FirstOrDefault(p => p.Id == curId);
            }

            AppSettings.SaveRootPath(RootPath);
            RefreshSaves();
            StatusMessage = "Loaded successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading: {ex.Message}";
        }
    }

    private void LoadSettings()
    {
        var settingsNode = _serverConfigRoot?["settings"]?.AsObject();
        if (settingsNode == null) { SettingGroups = []; return; }

        var groups = new Dictionary<string, SettingGroup>(StringComparer.Ordinal);
        foreach (var kvp in settingsNode)
        {
            var value = kvp.Value?.GetValue<string>() ?? "";
            var dot = kvp.Key.IndexOf('.');
            var groupName = dot >= 0 ? kvp.Key[..dot] : kvp.Key;
            var displayName = dot >= 0 ? kvp.Key[(dot + 1)..] : kvp.Key;

            if (!groups.TryGetValue(groupName, out var group))
            {
                group = new SettingGroup { Name = groupName };
                groups[groupName] = group;
            }
            group.Items.Add(new SettingItem { Key = kvp.Key, DisplayName = displayName, Value = value });
        }
        SettingGroups = [.. groups.Values.OrderBy(g => g.Name)];
    }

    private void LoadExpansionPresets()
    {
        if (!File.Exists(ExpansionsDataPath)) { ExpansionPresets = []; return; }
        try
        {
            var arr = JsonNode.Parse(File.ReadAllText(ExpansionsDataPath))?.AsArray();
            if (arr == null) { ExpansionPresets = []; return; }

            var presets = new List<ExpansionPreset>();
            foreach (var node in arr)
            {
                if (node == null) continue;
                presets.Add(new ExpansionPreset
                {
                    Id = node["Id"]?.GetValue<int>() ?? 0,
                    Name = node["Name"]?.GetValue<string>() ?? "Unknown",
                    RawJson = node.ToJsonString()
                });
            }
            ExpansionPresets = presets;
        }
        catch { ExpansionPresets = []; }
    }

    [RelayCommand]
    private async Task AddDataDir()
    {
        if (_window == null) return;
        var result = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select UO Client Data Folder"
        });
        if (result.Count > 0)
            DataDirectories.Add(result[0].Path.LocalPath);
    }

    [RelayCommand]
    private void RemoveDataDir()
    {
        if (SelectedDataDir == null) return;
        DataDirectories.Remove(SelectedDataDir);
        SelectedDataDir = null;
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            if (_serverConfigRoot == null) { StatusMessage = "Nothing loaded."; return; }

            var newDirs = new JsonArray();
            foreach (var dir in DataDirectories)
                newDirs.Add(JsonValue.Create(dir));
            _serverConfigRoot["dataDirectories"] = newDirs;

            if (_serverConfigRoot["settings"]?.AsObject() is { } settingsObj)
                foreach (var group in SettingGroups)
                    foreach (var item in group.Items)
                        settingsObj[item.Key] = JsonValue.Create(item.Value);

            File.WriteAllText(ModernUOJsonPath, _serverConfigRoot.ToJsonString(_writeOpts));

            if (SelectedPreset?.RawJson is { Length: > 0 } raw)
            {
                File.WriteAllText(ExpansionJsonPath, JsonNode.Parse(raw)!.ToJsonString(_writeOpts));
                CurrentExpansionName = SelectedPreset.Name;
            }

            StatusMessage = "Saved successfully.";
        }
        catch (Exception ex) { StatusMessage = $"Error saving: {ex.Message}"; }
    }

    // ── Build & Run ──────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStartProcess))]
    private async Task Build()
    {
        Save();
        ClearOutput();
        KillExistingModernUO();
        await RunProcess(BuildCommandText, RootPath, waitForExit: true);
    }

    [RelayCommand(CanExecute = nameof(CanStartProcess))]
    private async Task Run()
    {
        Save();
        ClearOutput();
        KillExistingModernUO();
        await RunProcess(RunCommandText, DistributionPath, waitForExit: false);
    }

    [RelayCommand(CanExecute = nameof(CanStartProcess))]
    private async Task BuildAndRun()
    {
        Save();
        ClearOutput();
        KillExistingModernUO();
        var exitCode = await RunProcess(BuildCommandText, RootPath, waitForExit: true);
        if (exitCode == 0)
            await RunProcess(RunCommandText, DistributionPath, waitForExit: false);
        else
            AppendOutput("\n[Build failed — server not started]\n");
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        try
        {
            _runningProcess?.Kill(entireProcessTree: true);
        }
        catch { /* already exited */ }
    }

    [RelayCommand]
    private void ClearOutput()
    {
        _outputBuilder.Clear();
        OutputText = "";
    }

    private bool CanStartProcess() => !IsBusy && !string.IsNullOrEmpty(RootPath);
    private bool CanStop() => IsBusy;

    private void KillExistingModernUO()
    {
        if (_runningProcess != null)
        {
            try { _runningProcess.Kill(entireProcessTree: true); } catch { }
        }
        foreach (var p in Process.GetProcessesByName("ModernUO"))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            p.Dispose();
        }
    }

    private async Task<int> RunProcess(string command, string workDir, bool waitForExit)
    {
        // Split "cmd /c publish.cmd" → ("cmd", "/c publish.cmd")
        var spaceIdx = command.IndexOf(' ');
        var fileName = spaceIdx >= 0 ? command[..spaceIdx] : command;
        var args = spaceIdx >= 0 ? command[(spaceIdx + 1)..] : "";

        // Resolve bare filenames (e.g. "ModernUO.exe") against the working directory,
        // since ProcessStartInfo with UseShellExecute=false doesn't search workDir.
        if (!Path.IsPathRooted(fileName) && !fileName.Contains(Path.DirectorySeparatorChar))
        {
            var candidate = Path.Combine(workDir, fileName);
            if (File.Exists(candidate))
                fileName = candidate;
        }

        IsBusy = true;
        AppendOutput($"$ {command}\n");

        try
        {
            var psi = new ProcessStartInfo(fileName, args)
            {
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _runningProcess = proc;

            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    Dispatcher.UIThread.Post(() => AppendOutput(e.Data + "\n"));
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    Dispatcher.UIThread.Post(() => AppendOutput(e.Data + "\n"));
            };
            proc.Exited += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    AppendOutput($"\n[Process exited with code {proc.ExitCode}]\n");
                    IsBusy = false;
                    if (_runningProcess == proc)
                        _runningProcess = null;
                });
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            if (waitForExit)
            {
                await proc.WaitForExitAsync();
                return proc.ExitCode;
            }

            return 0;
        }
        catch (Exception ex)
        {
            AppendOutput($"[Error: {ex.Message}]\n");
            IsBusy = false;
            _runningProcess = null;
            return -1;
        }
    }

    private void AppendOutput(string text)
    {
        _outputBuilder.Append(text);
        OutputText = _outputBuilder.ToString();
        OutputAppended?.Invoke();
    }

    // ── Save Management ──────────────────────────────────────────────────────

    [RelayCommand]
    private void RefreshSaves()
    {
        try
        {
            var descriptions = LoadDescriptions();
            var entries = new ObservableCollection<SaveEntry>();

            if (Directory.Exists(BackupsPath))
            {
                foreach (var dir in Directory.GetDirectories(BackupsPath)
                    .Where(d => !Path.GetFileName(d).Equals("Automatic", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(Directory.GetCreationTime))
                {
                    var name = Path.GetFileName(dir);
                    entries.Add(new SaveEntry
                    {
                        FolderName = name,
                        FullPath = dir,
                        Description = descriptions.TryGetValue(name, out var desc) ? desc : ""
                    });
                }
            }

            SaveEntries = entries;

            if (SelectedSaveEntry != null && !entries.Any(e => e.FolderName == SelectedSaveEntry.FolderName))
                SelectedSaveEntry = null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading saves: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRestoreSave))]
    private async Task RestoreSave()
    {
        if (SelectedSaveEntry == null) return;

        try
        {
            IsBusy = true;
            StatusMessage = $"Restoring '{SelectedSaveEntry.FolderName}' to Saves/...";

            var sourcePath = SelectedSaveEntry.FullPath;
            var destPath = SavesPath;

            await Task.Run(() =>
            {
                if (Directory.Exists(destPath))
                {
                    foreach (var file in Directory.GetFiles(destPath, "*", SearchOption.AllDirectories))
                        File.Delete(file);
                    foreach (var dir in Directory.GetDirectories(destPath))
                        Directory.Delete(dir, recursive: true);
                }

                CopyDirectory(sourcePath, destPath);
            });

            StatusMessage = $"Restored '{SelectedSaveEntry.FolderName}' to Saves/ successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error restoring save: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRestoreSave() => SelectedSaveEntry != null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanUpdateDescription))]
    private void UpdateSaveDescription()
    {
        if (SelectedSaveEntry == null) return;
        SelectedSaveEntry.Description = SelectedSaveDescription;
        PersistDescriptions();
        StatusMessage = $"Description saved for '{SelectedSaveEntry.FolderName}'.";
    }

    private bool CanUpdateDescription() => SelectedSaveEntry != null;

    [RelayCommand]
    private void DeleteSave(SaveEntry entry)
    {
        try
        {
            Directory.Delete(entry.FullPath, recursive: true);
            SaveEntries.Remove(entry);
            if (SelectedSaveEntry == entry)
                SelectedSaveEntry = null;
            PersistDescriptions();
            StatusMessage = $"Deleted backup '{entry.FolderName}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error deleting '{entry.FolderName}': {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RenameSave(SaveEntry entry)
    {
        if (_window == null) return;

        var newName = await new RenameDialog(entry.FolderName).ShowDialog<string?>(_window);
        if (string.IsNullOrEmpty(newName) || newName == entry.FolderName) return;

        var newPath = Path.Combine(BackupsPath, newName);
        if (Directory.Exists(newPath))
        {
            StatusMessage = $"A backup named '{newName}' already exists.";
            return;
        }

        try
        {
            Directory.Move(entry.FullPath, newPath);
            entry.FolderName = newName;
            entry.FullPath = newPath;
            PersistDescriptions();
            StatusMessage = $"Renamed to '{newName}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error renaming: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanBackup))]
    private async Task BackupCurrentSaves()
    {
        if (_window == null) return;

        var result = await new BackupDialog().ShowDialog<BackupDialogResult?>(_window);
        if (result == null) return;

        var destPath = Path.Combine(BackupsPath, result.Name);
        if (Directory.Exists(destPath))
        {
            StatusMessage = $"A backup named '{result.Name}' already exists.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"Backing up Saves/ to '{result.Name}'...";

            await Task.Run(() =>
            {
                if (!Directory.Exists(SavesPath))
                    throw new DirectoryNotFoundException("Saves/ folder not found.");
                CopyDirectory(SavesPath, destPath);
            });

            RefreshSaves();

            if (!string.IsNullOrEmpty(result.Description))
            {
                var entry = SaveEntries.FirstOrDefault(e => e.FolderName == result.Name);
                if (entry != null)
                {
                    entry.Description = result.Description;
                    PersistDescriptions();
                }
            }

            StatusMessage = $"Backup '{result.Name}' created.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error creating backup: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanBackup() => !IsBusy;

    private Dictionary<string, string> LoadDescriptions()
    {
        if (!File.Exists(DescriptionsFilePath)) return [];
        try
        {
            var obj = JsonNode.Parse(File.ReadAllText(DescriptionsFilePath))?.AsObject();
            if (obj == null) return [];
            return obj.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.GetValue<string>() ?? "");
        }
        catch { return []; }
    }

    private void PersistDescriptions()
    {
        try
        {
            var obj = new JsonObject();
            foreach (var entry in SaveEntries)
                if (!string.IsNullOrEmpty(entry.Description))
                    obj[entry.FolderName] = JsonValue.Create(entry.Description);

            if (Directory.Exists(BackupsPath))
                File.WriteAllText(DescriptionsFilePath, obj.ToJsonString(_writeOpts));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving descriptions: {ex.Message}";
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }
}
