# Compatibility

## Supported baseline

- Windows 11 x64, build 22000 or later
- Standard-user execution; administrator privileges are not required
- Center-aligned taskbar for the embedded taskbar surface
- Current Windows default render endpoint for master volume and mute
- Per-user notification-area lifetime and optional sign-in startup

## Bluetooth audio

QuickPods combines paired association data, Windows audio endpoints, and physical-device identity. Direct connection and disconnection are enabled only when the selected device's driver exposes a supported, unambiguous operation.

Devices may expose A2DP render, Hands-Free capture/render, transport, and other endpoints. QuickPods groups these into one user-visible row and targets the verified stereo render endpoint for default-output behavior.

Hardware and drivers differ. Unsupported or uncertain devices remain visible when possible but use Windows Bluetooth or Sound Settings for mutation. RDP Remote Audio is not considered local Bluetooth inventory.

## Display and shell

QuickPods supports normal Windows light, dark, and system themes and common Windows 11 DPI values. The taskbar host validates physical-pixel geometry for the active monitor and Explorer generation.

Left-aligned taskbars do not show the embedded surface. Incomplete taskbar identity, geometry, monitor, DPI, or obstacle evidence also hides the surface and keeps notification-area access available.

Mixed-DPI moves, monitor hot-plug, Windows Insider builds, third-party taskbar replacements, and shell customization tools may require additional validation even when the general Windows baseline is met.
