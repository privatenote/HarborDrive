using System.Windows;
using System.Windows.Controls;
using HarborDrive.Core;

namespace HarborDrive.App;

public partial class ConnectionWindow : Window
{
    private readonly DriveProfile original;
    private readonly MountManager manager;
    private readonly string[] used;
    private bool working;
    public DriveProfile? Result { get; private set; }
    public Credentials Credentials => new(AccessBox.Password.Trim(), SecretBox.Password.Trim(), TokenBox.Password.Trim());
    public ConnectionWindow(DriveProfile? profile, Credentials credentials, string[] used, MountManager manager)
    {
        InitializeComponent(); this.manager = manager; this.used = used;
        MaxHeight = SystemParameters.WorkArea.Height - 24;
        original = profile ?? new();
        NameBox.Text = original.Name; RegionBox.Text = original.Region; BucketBox.Text = original.Bucket; PrefixBox.Text = original.Prefix;
        EndpointBox.Text = original.Endpoint; CacheRootBox.Text = original.CacheRoot;
        CacheSizeBox.Text = original.CacheGiB.ToString(); FreeSizeBox.Text = original.MinFreeGiB.ToString(); WriteBackBox.Text = original.WriteBackSeconds.ToString();
        AccessBox.Password = credentials.AccessKey; SecretBox.Password = credentials.SecretKey; TokenBox.Password = credentials.SessionToken;
        AwsProfileBox.Text = original.AwsProfile;
        var letters = Enumerable.Range('D', 23).Select(x => ((char)x).ToString()).Where(x => !used.Contains(x, StringComparer.OrdinalIgnoreCase)).ToArray();
        LetterBox.ItemsSource = letters; LetterBox.SelectedItem = letters.Contains(original.DriveLetter) ? original.DriveLetter : letters.FirstOrDefault();
        AutoMountBox.IsChecked = original.AutoMount; RestartBox.IsChecked = original.AutoRestart; ReadOnlyBox.IsChecked = original.ReadOnly; PathStyleBox.IsChecked = original.ForcePathStyle;
        EnvironmentBox.IsChecked = original.UseEnvironmentAuth; UpdateAuth();
        Closing += (_, e) => { if (working) e.Cancel = true; };
    }
    private DriveProfile Read(bool buckets = false)
    {
        if (!int.TryParse(CacheSizeBox.Text, out var cache) || !int.TryParse(FreeSizeBox.Text, out var free) || !int.TryParse(WriteBackBox.Text, out var write)) throw new ArgumentException("캐시 용량과 쓰기 지연에는 정수를 입력해 주세요.");
        var p = original with { Name = NameBox.Text.Trim(), Region = RegionBox.Text.Trim(), Bucket = buckets && BucketBox.Text.Trim().Length == 0 ? "bucket-placeholder" : BucketBox.Text.Trim(), Prefix = PrefixBox.Text.Trim().Trim('/'), Endpoint = EndpointBox.Text.Trim(), DriveLetter = LetterBox.SelectedItem as string ?? "", UseEnvironmentAuth = EnvironmentBox.IsChecked == true, AwsProfile = AwsProfileBox.Text.Trim(), AutoMount = AutoMountBox.IsChecked == true, AutoRestart = RestartBox.IsChecked == true, ReadOnly = ReadOnlyBox.IsChecked == true, ForcePathStyle = PathStyleBox.IsChecked == true, CacheRoot = CacheRootBox.Text.Trim(), CacheGiB = cache, MinFreeGiB = free, WriteBackSeconds = write };
        p.Validate();
        if (!p.UseEnvironmentAuth && (Credentials.AccessKey.Length == 0 || Credentials.SecretKey.Length == 0)) throw new ArgumentException("Access Key와 Secret Key를 입력해 주세요.");
        if (used.Contains(p.DriveLetter, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("이미 사용 중인 드라이브 문자입니다.");
        return p;
    }
    private void UpdateAuth() { if (AccessBox == null) return; AccessBox.IsEnabled = SecretBox.IsEnabled = TokenBox.IsEnabled = EnvironmentBox.IsChecked != true; AwsProfileBox.IsEnabled = EnvironmentBox.IsChecked == true; }
    private void AuthChanged(object sender, RoutedEventArgs e) => UpdateAuth();
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try { Result = Read(); DialogResult = true; }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
    private async Task Browse(bool buckets, bool test)
    {
        if (working) return;
        working = true; SaveButton.IsEnabled = TestButton.IsEnabled = false; ErrorText.Text = "S3 연결 확인 중…";
        try
        {
            var p = Read(buckets);
            var entries = await manager.ListDirectories(p, Credentials, buckets);
            if (test) { ErrorText.Text = $"연결 확인 완료 · 현재 경로의 하위 폴더 {entries.Length}개"; return; }
            var list = new ListBox { ItemsSource = entries, Margin = new Thickness(18), MinWidth = 350, MinHeight = 250 };
            var panel = new DockPanel();
            var button = new Button { Content = "선택", Margin = new Thickness(18), IsDefault = true };
            DockPanel.SetDock(button, Dock.Bottom); panel.Children.Add(button); panel.Children.Add(list);
            var picker = new Window { Title = buckets ? "버킷 선택" : "하위 폴더 선택", Content = panel, Owner = this, Width = 490, Height = 430, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            button.Click += (_, _) => { if (list.SelectedItem != null) picker.DialogResult = true; };
            list.MouseDoubleClick += (_, _) => { if (list.SelectedItem != null) picker.DialogResult = true; };
            if (picker.ShowDialog() == true && list.SelectedItem is string selected)
            {
                if (buckets) { BucketBox.Text = selected.TrimEnd('/'); PrefixBox.Clear(); }
                else PrefixBox.Text = string.Join('/', new[] { PrefixBox.Text.Trim('/'), selected.Trim('/') }.Where(x => x.Length > 0));
            }
            ErrorText.Text = entries.Length == 0 ? "현재 경로에 하위 폴더가 없습니다. 경로를 직접 입력할 수도 있습니다." : "목록 확인 완료. 버킷 목록 권한이 없으면 버킷 이름을 직접 입력하세요.";
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
        finally { working = false; SaveButton.IsEnabled = TestButton.IsEnabled = true; }
    }
    private async void Buckets_Click(object sender, RoutedEventArgs e) => await Browse(true, false);
    private async void Folders_Click(object sender, RoutedEventArgs e) => await Browse(false, false);
    private async void Test_Click(object sender, RoutedEventArgs e) => await Browse(false, true);
}
