Option Explicit

If WScript.Arguments.Count <> 2 Then
    WScript.Echo "Usage: Set-InstallerSummary.vbs <msi-path> <package-code>"
    WScript.Quit 2
End If

Dim installerPath
Dim packageCode
Dim installer
Dim database
Dim summary
Dim fixedTimestamp

installerPath = WScript.Arguments(0)
packageCode = WScript.Arguments(1)
fixedTimestamp = DateSerial(2000, 1, 1) + TimeSerial(0, 0, 0)

Set installer = CreateObject("WindowsInstaller.Installer")
Set database = installer.OpenDatabase(installerPath, 2)
Set summary = database.SummaryInformation(3)
summary.Property(9) = packageCode
summary.Property(12) = fixedTimestamp
summary.Property(13) = fixedTimestamp
summary.Persist

Set summary = Nothing
Set database = Nothing
Set installer = Nothing
