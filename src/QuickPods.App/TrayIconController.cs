using System.Drawing;
using System.Windows.Forms;
using QuickPods.Presentation;

namespace QuickPods.App;

internal sealed class TrayIconController : IDisposable
{
    private readonly ContextMenuStrip menu;
    private readonly Icon icon;
    private readonly NotifyIcon notifyIcon;
    private readonly ProductLocalizer localizer;
    private readonly ToolStripMenuItem openItem;
    private readonly ToolStripMenuItem refreshItem;
    private readonly ToolStripMenuItem settingsItem;
    private readonly ToolStripMenuItem taskbarSurfaceItem;
    private readonly ToolStripMenuItem soundSettingsItem;
    private readonly ToolStripMenuItem bluetoothSettingsItem;
    private readonly ToolStripMenuItem exitItem;
    private bool applyingTaskbarSurfaceState;
    private bool disposed;

    internal TrayIconController(
        Action open,
        Action refresh,
        Action openSettings,
        Action<bool> setTaskbarSurfaceVisible,
        Action openSoundSettings,
        Action openBluetoothSettings,
        Action exit,
        ProductLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(openSettings);
        ArgumentNullException.ThrowIfNull(setTaskbarSurfaceVisible);
        ArgumentNullException.ThrowIfNull(openSoundSettings);
        ArgumentNullException.ThrowIfNull(openBluetoothSettings);
        ArgumentNullException.ThrowIfNull(exit);
        this.localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));

        Icon sourceIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty) ??
            SystemIcons.Application;
        icon = (Icon)sourceIcon.Clone();
        menu = new ContextMenuStrip();

        openItem = new ToolStripMenuItem();
        openItem.Font = new Font(openItem.Font, FontStyle.Bold);
        openItem.Click += (_, _) => open();
        menu.Items.Add(openItem);
        refreshItem = new ToolStripMenuItem(null, null, (_, _) => refresh());
        menu.Items.Add(refreshItem);
        settingsItem = new ToolStripMenuItem(null, null, (_, _) => openSettings());
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        taskbarSurfaceItem = new ToolStripMenuItem
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
        soundSettingsItem = new ToolStripMenuItem(null, null, (_, _) => openSoundSettings());
        menu.Items.Add(soundSettingsItem);
        bluetoothSettingsItem = new ToolStripMenuItem(
            null,
            null,
            (_, _) => openBluetoothSettings());
        menu.Items.Add(bluetoothSettingsItem);
        menu.Items.Add(new ToolStripSeparator());
        exitItem = new ToolStripMenuItem(null, null, (_, _) => exit());
        menu.Items.Add(exitItem);

        notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = icon,
            Text = "QuickPods",
            Visible = true,
        };
        notifyIcon.DoubleClick += (_, _) => open();
        ApplyLocalization();
    }

    internal void ApplyLocalization()
    {
        openItem.Text = localizer["TrayOpen"];
        refreshItem.Text = localizer["TrayRefresh"];
        settingsItem.Text = localizer["TraySettings"];
        taskbarSurfaceItem.Text = localizer["TrayTaskbarBar"];
        soundSettingsItem.Text = localizer["TraySoundSettings"];
        bluetoothSettingsItem.Text = localizer["TrayBluetoothSettings"];
        exitItem.Text = localizer["TrayExit"];
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
