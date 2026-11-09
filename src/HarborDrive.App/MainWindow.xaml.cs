using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HarborDrive.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace HarborDrive.App;

public sealed class DriveRow(DriveStatus status) : INotifyPropertyChanged
{
    public DriveStatus Status { get; } = status;
    public string Name => Status.Profile.Name;
    public string Subtitle => Status.Profile.MountPoint + "   ·   " + (Status.State switch { MountState.Connected => "연결됨", MountState.Uploading => "전송 중", MountState.Connecting => "연결 중", MountState.Retrying => "복구 중", MountState.Unknown => "확인 필요", MountState.Error => "오류", _ => "연결 해제" });
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}

public partial class MainWindow : Window
{
    private readonly AppStore store;
    private readonly AppSettings settings;
    private readonly MountManager manager;
    private readonly ObservableCollection<DriveRow> rows = [];
    private readonly Forms.NotifyIcon tray;
    private readonly TrayIconSet trayIcons;
    private readonly GitHubUpdates updates = new();
    private bool checkingUpdates;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool busy;
    private bool exiting;
    private bool initializing = true;
    private bool notifiedClose;
    private DateTimeOffset lastLog;
    private readonly Dictionary<string, MountState> lastStates = [];
    private DriveProfile? Selected => (DriveList.SelectedItem as DriveRow)?.Status.Profile;

    public MainWindow(AppStore store, AppSettings settings)
    {
        InitializeComponent();
        VersionText.Text = $"v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3)} · Windows {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}";
        this.store = store; this.settings = settings;
        AutoUpdatesMenu.IsChecked = settings.CheckForUpdates;
        manager = new MountManager(store, FindEngine());
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            settings.StartAtLogin = string.Equals(run?.GetValue("HarborDrive") as string, "\"" + Environment.ProcessPath + "\" --background", StringComparison.OrdinalIgnoreCase);
            LoginStart.IsChecked = settings.StartAtLogin;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            LoginStart.IsEnabled = false;
            LoginStart.ToolTip = "Windows 시작 설정을 읽을 수 없습니다.";
        }
        DriveList.ItemsSource = rows;
        foreach (var p in settings.Profiles) rows.Add(new(manager.GetStatus(p)));
        if (rows.Count > 0) DriveList.SelectedIndex = 0;
        trayIcons = new TrayIconSet();
        tray = new Forms.NotifyIcon { Text = "HarborDrive", Icon = trayIcons.Disconnected, Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("HarborDrive 열기", null, (_, _) => Dispatcher.Invoke(ShowDashboard));
        menu.Items.Add("모든 연결 해제 후 종료", null, (_, _) => Dispatcher.Invoke(async () => await ExitApp()));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowDashboard);
        Closing += (_, e) =>
        {
            if (exiting) return;
            e.Cancel = true; Hide();
            if (!notifiedClose) { tray.ShowBalloonTip(3000, "HarborDrive", "트레이에서 실행 중입니다. 드라이브 연결과 전송을 계속 유지합니다.", Forms.ToolTipIcon.Info); notifiedClose = true; }
        };
        timer.Tick += async (_, _) =>
        {
            if (busy || initializing) return;
            try { await manager.Poll(); Render(); }
            catch (Exception e) { FooterText.Text = e.Message; }
        };
        // BeginInvoke also runs when --background starts without showing a window.
        Dispatcher.BeginInvoke(async () =>
        {
            try { await manager.Initialize(settings.Profiles); }
            catch (Exception e) { ShowError(e); }
            finally { initializing = false; Render(); timer.Start(); }
            if (settings.CheckForUpdates) await CheckUpdates(false);
        });
        Render();
    }
    private static string FindEngine()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "rclone.exe");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "tools", "rclone.exe");
    }
    public void ShowDashboard() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void Render()
    {
        var dependency = !File.Exists(manager.RclonePath) ? "전송 엔진 파일이 없습니다. HarborDrive 설치 파일을 다시 실행해 주세요." : !MountManager.WinFspInstalled ? "드라이브 연결 구성요소가 없습니다. HarborDrive 설치 파일을 실행해 WinFsp를 설치해 주세요." : "";
        DependencyBanner.Visibility = dependency.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DependencyText.Text = dependency;
        EmptyCard.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Details.Visibility = Selected == null ? Visibility.Collapsed : Visibility.Visible;
        foreach (var row in rows)
        {
            row.Refresh();
            if (lastStates.TryGetValue(row.Status.Profile.Id, out var prior) && prior != row.Status.State && row.Status.State is MountState.Error or MountState.Unknown)
                tray.ShowBalloonTip(3000, row.Name + " · 확인 필요", row.Status.Message.Length > 220 ? row.Status.Message[..220] : row.Status.Message, Forms.ToolTipIcon.Warning);
            lastStates[row.Status.Profile.Id] = row.Status.State;
        }
        if (tray != null) tray.Icon = trayIcons.For(rows.Select(row => row.Status));
        if (Selected is not { } p) return;
        var status = manager.GetStatus(p); var s = status.Snapshot;
        LetterText.Text = p.MountPoint; NameText.Text = p.Name;
        RemoteText.Text = p.DisplayRemote + (p.ReadOnly ? "  ·  읽기 전용" : "  ·  읽기 / 쓰기");
        StateText.Text = status.Message;
        StateBadge.Background = new SolidColorBrush(status.State is MountState.Error or MountState.Unknown ? Color.FromRgb(255, 239, 221) : Color.FromRgb(232, 244, 241));
        PendingText.Text = s == null ? "—" : $"{s.Queued} / {s.Uploading}";
        SpeedText.Text = s == null ? "—" : Units.Bytes(s.Speed) + "/s";
        TransferredText.Text = s == null ? "현재 연결 세션 기준" : "누적 전송 " + Units.Bytes(s.Bytes);
        CacheText.Text = s == null ? "—" : Units.Bytes(s.CacheBytes);
        CacheLimitText.Text = $"목표 {p.CacheGiB} GB · 여유 공간 {p.MinFreeGiB} GB";
        QueueHint.Text = s == null ? "연결 후 전송 상태를 확인할 수 있습니다." : s.Queue.Count == 0 ? "현재 업로드 대기열이 비어 있습니다." : "저장된 파일을 S3로 전송합니다. 실패한 파일은 자동 재시도합니다.";
        QueueGrid.ItemsSource = s?.Queue.Select(q => new { q.Name, SizeText = Units.Bytes(q.Size), StateText = q.Uploading ? "전송 중" : q.Tries > 0 ? "재시도 대기" : "대기", q.Tries }).ToArray();
        ConnectButton.IsEnabled = !busy && !initializing && status.State is not (MountState.Connected or MountState.Uploading or MountState.Connecting);
        DisconnectButton.IsEnabled = !busy && !initializing && manager.IsManaged(p.Id);
        EditButton.IsEnabled = !busy && !initializing && !manager.IsManaged(p.Id);
        OpenButton.IsEnabled = !busy && status.State is MountState.Connected or MountState.Uploading or MountState.Error;
        FooterText.Text = status.UpdatedAt is { } time ? "마지막 상태 확인 " + time.ToLocalTime().ToString("HH:mm:ss") : "연결 후 상태가 표시됩니다.";
        if (DateTimeOffset.Now - lastLog > TimeSpan.FromSeconds(8)) RefreshLog();
    }
    private void RefreshLog()
    {
        if (Selected is not { } p) return;
        try { LogText.Text = MountManager.Redact(store.ReadLog(p), store.GetCredentials(p.Id)); LogText.ScrollToEnd(); lastLog = DateTimeOffset.Now; }
        catch (IOException e) { LogText.Text = e.Message; }
    }
    private async Task Run(Func<Task> action)
    {
        if (busy || initializing) return;
        busy = true; Render();
        try { await action(); }
        catch (Exception ex) { if (Selected is { } p) manager.SetError(p, ex); ShowError(ex); }
        finally { busy = false; Render(); }
    }
    private void ShowError(Exception e) => System.Windows.MessageBox.Show(this, e.Message, "HarborDrive · 확인 필요", MessageBoxButton.OK, MessageBoxImage.Warning);
    private void Add_Click(object sender, RoutedEventArgs e) => EditProfile(null);
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu is { } menu) { menu.PlacementTarget = button; menu.IsOpen = true; }
    }
    private void About_Click(object sender, RoutedEventArgs e) => System.Windows.MessageBox.Show(this,
        VersionText.Text + "\n\nHarborDrive · S3 네트워크 드라이브 (MIT)\nhttps://github.com/privatenote/HarborDrive\n전송 엔진: rclone (MIT)\n\nWinFsp - Windows File System Proxy, Copyright (C) Bill Zissimopoulos\nhttps://github.com/winfsp/winfsp\n\n설치 폴더의 LICENSE, THIRD-PARTY-NOTICES.md와 licenses 폴더에서 라이선스를 확인할 수 있습니다.",
        "HarborDrive 정보", MessageBoxButton.OK, MessageBoxImage.Information);
    private async void UpdateCheck_Click(object sender, RoutedEventArgs e) => await CheckUpdates(true);
    private void UpdateRelease_Click(object sender, RoutedEventArgs e) => OpenReleasePage();
    private void OpenReleasePage()
    {
        try { Process.Start(new ProcessStartInfo(GitHubUpdates.ReleasePage.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError(ex); }
    }
    private async Task CheckUpdates(bool manual)
    {
        if (checkingUpdates || exiting) return;
        checkingUpdates = true; UpdateMenu.IsEnabled = false;
        try
        {
            var current = typeof(MainWindow).Assembly.GetName().Version!;
            var result = await updates.Check(current);
            if (exiting) return;
            if (result.State == UpdateState.Available)
            {
                var version = result.Version!.ToString(3);
                UpdateNoticeText.Text = $"HarborDrive {version} 업데이트를 사용할 수 있습니다.";
                UpdateNotice.Visibility = Visibility.Visible;
                UpdateMenu.Header = $"새 버전 {version} · 업데이트 확인…";
                if (manual)
                {
                    if (System.Windows.MessageBox.Show(this, $"새 버전 {version}이 있습니다.\n\nGitHub에서 설치 파일을 받은 뒤, 이 앱에서 '모든 연결 해제 후 종료'를 선택하고 설치하세요. 설정과 캐시는 유지됩니다.\n\n릴리스 페이지를 열까요?", "HarborDrive 업데이트", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes) OpenReleasePage();
                }
                else tray.ShowBalloonTip(4000, "HarborDrive 업데이트", $"새 버전 {version}이 있습니다. 앱의 설정 메뉴에서 확인하세요.", Forms.ToolTipIcon.Info);
            }
            else if (manual)
            {
                var message = result.State switch
                {
                    UpdateState.NotPublished => "아직 등록된 정식 배포가 없습니다.",
                    UpdateState.PackageMissing => "새 버전이 등록되었지만 x64 설치 파일은 아직 준비되지 않았습니다.",
                    _ => "현재 버전이 최신 버전입니다."
                };
                System.Windows.MessageBox.Show(this, message, "HarborDrive 업데이트", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            if (manual && !exiting) ShowError(new IOException("업데이트를 확인하지 못했습니다. 네트워크 연결 또는 GitHub 요청 제한을 확인해 주세요.\n\n" + ex.Message));
        }
        finally { checkingUpdates = false; UpdateMenu.IsEnabled = true; }
    }
    private void AutoUpdates_Click(object sender, RoutedEventArgs e)
    {
        var previous = settings.CheckForUpdates;
        try { settings.CheckForUpdates = AutoUpdatesMenu.IsChecked; store.Save(settings); }
        catch (Exception ex) { settings.CheckForUpdates = previous; AutoUpdatesMenu.IsChecked = previous; ShowError(ex); }
    }
    private void Edit_Click(object sender, RoutedEventArgs e) { if (Selected is { } p) EditProfile(p); }
    private void EditProfile(DriveProfile? original)
    {
        if (busy || initializing) return;
        if (original != null && manager.IsManaged(original.Id)) return;
        var used = settings.Profiles.Where(p => p.Id != original?.Id).Select(p => p.DriveLetter).ToArray();
        var dialog = new ConnectionWindow(original, original == null ? new() : store.GetCredentials(original.Id), used, manager) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } profile) return;
        try
        {
            var targetChanged = original != null && (profile.Remote != original.Remote || profile.Endpoint != original.Endpoint || profile.CacheRoot != original.CacheRoot);
            if (targetChanged && manager.HasDirtyCache(original!)) throw new InvalidOperationException("이 연결에 미전송 캐시가 남아 있습니다. 기존 설정으로 연결해 전송한 뒤 저장 경로를 바꿔 주세요.");
            if (targetChanged) profile = profile with { Id = Guid.NewGuid().ToString("N") }; // Never reuse a cache for another remote.
            if (settings.Profiles.Any(p => p.Id != original?.Id && p.DriveLetter.Equals(profile.DriveLetter, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("다른 연결에서 사용 중인 드라이브 문자입니다.");
            var previous = settings.Profiles.ToList();
            store.SaveCredentials(profile.Id, dialog.Credentials);
            if (original != null) settings.Profiles.Remove(original);
            settings.Profiles.Add(profile);
            try { store.Save(settings); } catch { settings.Profiles = previous; throw; }
            if (original != null) { var row = rows.First(r => r.Status.Profile.Id == original.Id); rows.Remove(row); }
            var next = new DriveRow(manager.GetStatus(profile)); rows.Add(next); DriveList.SelectedItem = next; Render();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void Connect_Click(object sender, RoutedEventArgs e) { if (Selected is { } p) await Run(() => manager.Connect(p)); }
    private async void Disconnect_Click(object sender, RoutedEventArgs e) { if (Selected is { } p) await Run(() => manager.Disconnect(p)); }
    private async void Retry_Click(object sender, RoutedEventArgs e) { if (Selected is { } p) await Run(() => manager.RetryUploads(p)); }
    private async void Refresh_Click(object sender, RoutedEventArgs e) { if (Selected is { } p) await Run(() => manager.Refresh(p)); }
    private async void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        if (System.Windows.MessageBox.Show(this, "파일을 사용하는 프로그램을 먼저 닫아 주세요.\n\n미전송 파일은 로컬 캐시에 남기고 연결을 해제합니다. 같은 연결을 다시 시작하면 전송을 재개합니다. 캐시 폴더를 지우거나 경로를 바꾸지 마세요.\n\n중지할까요?", "캐시 보존 후 중지", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) await Run(() => manager.Disconnect(p, true));
    }
    private void OpenPath(string path) { try { Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = true }); } catch (Exception e) { ShowError(e); } }
    private void OpenLocalDirectory(string path)
    {
        try { Directory.CreateDirectory(path); OpenPath(LocalDirectoryPath.Resolve(path)); }
        catch (Exception e) { ShowError(e); }
    }
    private void Open_Click(object sender, RoutedEventArgs e) { if (Selected is { } p) OpenPath(p.MountPoint + "\\"); }
    private void Cache_Click(object sender, RoutedEventArgs e) { if (Selected is { } p) OpenLocalDirectory(store.CachePath(p)); }
    private void Logs_Click(object sender, RoutedEventArgs e) => OpenLocalDirectory(store.Logs);
    private void RefreshLog_Click(object sender, RoutedEventArgs e) => RefreshLog();
    private void DriveList_SelectionChanged(object sender, SelectionChangedEventArgs e) { lastLog = DateTimeOffset.MinValue; if (manager != null) Render(); }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p || busy || initializing) return;
        if (manager.IsManaged(p.Id) || manager.HasDirtyCache(p)) { ShowError(new InvalidOperationException("먼저 미전송 파일을 모두 전송하고 연결을 해제해 주세요.")); return; }
        if (System.Windows.MessageBox.Show(this, "연결 설정을 삭제할까요? S3 파일과 로컬 캐시 폴더는 보존됩니다.", "연결 설정 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { settings.Profiles.Remove(p); store.Save(settings); store.DeleteCredentials(p.Id); rows.Remove(rows.First(x => x.Status.Profile.Id == p.Id)); if (rows.Count > 0) DriveList.SelectedIndex = 0; Render(); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void LoginStart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (LoginStart.IsChecked == true) key.SetValue("HarborDrive", "\"" + Environment.ProcessPath + "\" --background");
            else key.DeleteValue("HarborDrive", false);
            settings.StartAtLogin = LoginStart.IsChecked == true; store.Save(settings);
        }
        catch (Exception ex) { LoginStart.IsChecked = settings.StartAtLogin; ShowError(ex); }
    }
    private async void Exit_Click(object sender, RoutedEventArgs e) => await ExitApp();
    internal Task FinishSmokeTest() => ExitApp();
    private async Task ExitApp()
    {
        if (busy || initializing) return;
        await Run(async () =>
        {
            foreach (var p in settings.Profiles) if (manager.IsManaged(p.Id)) await manager.Disconnect(p);
            exiting = true; timer.Stop(); tray.Visible = false; tray.Dispose(); trayIcons.Dispose(); updates.Dispose(); manager.Dispose();
            System.Windows.Application.Current.Shutdown();
        });
    }
}
