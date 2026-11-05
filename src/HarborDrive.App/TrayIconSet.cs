using System.Drawing;
using System.IO;
using HarborDrive.Core;

namespace HarborDrive.App;

internal sealed class TrayIconSet : IDisposable
{
    public Icon Connected { get; } = Load("TrayConnected");
    public Icon Disconnected { get; } = Load("TrayDisconnected");
    public Icon Transferring { get; } = Load("TrayTransferring");
    public Icon Error { get; } = Load("TrayError");
    private static Icon Load(string name)
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}.ico"))
            ?? throw new InvalidDataException("트레이 아이콘 리소스가 없습니다.");
        using var stream = resource.Stream;
        using var decoded = new Icon(stream, new Size(32, 32));
        return (Icon)decoded.Clone();
    }
    public Icon For(IEnumerable<DriveStatus> statuses)
    {
        var states = statuses.Select(s => s.State).ToArray();
        if (states.Any(s => s is MountState.Error or MountState.Unknown or MountState.Retrying)) return Error;
        if (states.Any(s => s is MountState.Uploading or MountState.Connecting)) return Transferring;
        return states.Contains(MountState.Connected) ? Connected : Disconnected;
    }
    public void Dispose() { Connected.Dispose(); Disconnected.Dispose(); Transferring.Dispose(); Error.Dispose(); }
}
