using System.Drawing;
using System.Windows.Forms;

namespace QuickPods.App;

internal sealed class TrayIconController : IDisposable
{
    private readonly ContextMenuStrip menu;
    private readonly Icon icon;
    private readonly NotifyIcon notifyIcon;
    private readonly ToolStripMenuItem taskbarSurfaceItem;
    private bool applyingTaskbarSurfaceState;
    private bool disposed;

    internal TrayIconController(
        Action open,
        Action refresh,
        Action<bool> setTaskbarSurfaceVisible,
        Action openSoundSettings,
        Action openBluetoothSettings,
        Action exit)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(setTaskbarSurfaceVisible);
        ArgumentNullException.ThrowIfNull(openSoundSettings);
        ArgumentNullException.ThrowIfNull(openBluetoothSettings);
        ArgumentNullException.ThrowIfNull(exit);

        Icon sourceIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty) ??
            SystemIcons.Application;
        icon = (Icon)sourceIcon.Clone();
        menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("QuickPods を開く");
        openItem.Font = new Font(openItem.Font, FontStyle.Bold);
        openItem.Click += (_, _) => open();
        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripMenuItem("更新", null, (_, _) => refresh()));
        taskbarSurfaceItem = new ToolStripMenuItem("タスクバー操作バーを表示")
        {
            CheckOnClick = true,
            Checked = true,
        };
        taskbarSurfaceItem.CheckedChanged += (_, _) =>
        {
            if (!applyingTaskbarSurfaceState)
            {
                setTaskbarSurfaceVisible(taskbarSurfaceItem.Checked);
            }
        };
        menu.Items.Add(taskbarSurfaceItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(
            "サウンド設定",
            null,
            (_, _) => openSoundSettings()));
        menu.Items.Add(new ToolStripMenuItem(
            "Bluetooth設定",
            null,
            (_, _) => openBluetoothSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("終了", null, (_, _) => exit()));

        notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = icon,
            Text = "QuickPods",
            Visible = true,
        };
        notifyIcon.DoubleClick += (_, _) => open();
    }

    internal void SetTaskbarSurfaceVisible(bool visible)
    {
        if (taskbarSurfaceItem.Checked != visible)
        {
            applyingTaskbarSurfaceState = true;
            try
            {
                taskbarSurfaceItem.Checked = visible;
            }
            finally
            {
                applyingTaskbarSurfaceState = false;
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        menu.Dispose();
        icon.Dispose();
    }
}
