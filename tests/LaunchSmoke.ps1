param([Parameter(Mandatory=$true)][string]$Exe)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
if(!('DeskMenuProbeNative' -as [type])){
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DeskMenuProbeNative {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window,uint message,IntPtr key,IntPtr point);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
}
'@
}
$scratch=Join-Path $PSScriptRoot ('launch-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
$copy=Join-Path $scratch '窗边手记.exe'
Copy-Item -LiteralPath $Exe -Destination $copy
$env:DESKATELIER_DATA=Join-Path $scratch '测试存档'
$process=Start-Process -FilePath $copy -WindowStyle Hidden -PassThru
$processMatch=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)
function Owned-Surfaces {
    return [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$processMatch)
}
function Find-OwnedWindow([string]$name) {
    foreach($surface in (Owned-Surfaces)) { if($surface.Current.Name -like $name) {return $surface} }
}
function Wait-OwnedWindow([string]$name,[bool]$visible) {
    for($i=0;$i -lt 65;$i++){
        if($process.HasExited){throw "Unexpected exit $($process.ExitCode)"}
        $w=Find-OwnedWindow $name
        if(([bool]$w) -eq $visible){return $w}
        Start-Sleep -Milliseconds 100
    }
    throw "Window not in expected state: $name / visible=$visible"
}
function Invoke-OwnedControl([string]$name) {
    $wanted=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$name)
    for($i=0;$i -lt 65;$i++){
        foreach($surface in (Owned-Surfaces)){
            try {
                foreach($item in $surface.FindAll([Windows.Automation.TreeScope]::Subtree,$wanted)){
                    $action=$null
                    if($item.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern,[ref]$action)){
                        ([Windows.Automation.InvokePattern]$action).Invoke()
                        return
                    }
                }
            } catch [Windows.Automation.ElementNotAvailableException] { if($process.HasExited){return} }
        }
        Start-Sleep -Milliseconds 100
    }
    throw "Missing interactive control $name"
}
function Open-MenuThroughSecondLaunch {
    $other=Start-Process -FilePath $copy -WindowStyle Hidden -PassThru
    if(!$other.WaitForExit(6000) -or $other.ExitCode -ne 0){throw 'Second launch did not hand off to the original process'}
}
function Open-MenuThroughPet($pet) {
    $bounds=$pet.Current.BoundingRectangle
    $x=[int]($bounds.Width/2)
    $y=[int]($bounds.Height*0.70)
    $point=[IntPtr](($y -shl 16) -bor $x)
    $window=[IntPtr]$pet.Current.NativeWindowHandle
    $saved=[DeskMenuProbeNative+Point]::new()
    if(![DeskMenuProbeNative]::GetCursorPos([ref]$saved)){throw 'Could not locate the test desktop cursor'}
    $aim=[DeskMenuProbeNative+Point]::new();$aim.X=[int]$bounds.Left+$x;$aim.Y=[int]$bounds.Top+$y
    try {
        if(![DeskMenuProbeNative]::SetCursorPos($aim.X,$aim.Y)){throw 'The test desktop cursor could not reach the pet'}
        Start-Sleep -Milliseconds 120
        if([DeskMenuProbeNative]::WindowFromPoint($aim) -ne $window){throw 'Test pet does not own the sample point'}
        if(![DeskMenuProbeNative]::PostMessage($window,0x0204,[IntPtr]2,$point) -or
            ![DeskMenuProbeNative]::PostMessage($window,0x0205,[IntPtr]0,$point)){throw 'Could not post right mouse input to isolated pet window'}
        Start-Sleep -Milliseconds 220
    } finally {
        $after=[DeskMenuProbeNative+Point]::new()
        if([DeskMenuProbeNative]::GetCursorPos([ref]$after) -and $after.X -eq $aim.X -and $after.Y -eq $aim.Y){
            [void][DeskMenuProbeNative]::SetCursorPos($saved.X,$saved.Y)
        }
    }
}
try {
    $pet=Wait-OwnedWindow '桌面伙伴' $true
    Start-Sleep -Milliseconds 320
    if(Find-OwnedWindow '窗边手记*'){throw 'Unexpected dashboard or tool at startup'}
    Write-Output 'PASS direct EXE launch shows only the pet and no tool window'
    Open-MenuThroughPet $pet
    Invoke-OwnedControl '番茄钟'
    $card=Wait-OwnedWindow '窗边手记 · 番茄钟' $true
    $bounds=$card.Current.BoundingRectangle
    if($bounds.Width -lt 100 -or $bounds.Height -lt 80){throw 'Invalid native card bounds'}
    Write-Output ("PASS right mouse messages sent to the pet HWND opened its native menu; focus card physical pixels "+$bounds.Width+"x"+$bounds.Height)
    Invoke-OwnedControl '开始专注'
    Invoke-OwnedControl '暂停'
    Invoke-OwnedControl '关闭'
    [void](Wait-OwnedWindow '窗边手记 · 番茄钟' $false)
    if($process.HasExited){throw 'Closing a tool terminated the app'}
    Write-Output 'PASS actual focus controls function and closing the transient card leaves the pet running'
    Open-MenuThroughSecondLaunch
    Invoke-OwnedControl '退出'
    if(!$process.WaitForExit(6000) -or $process.ExitCode -ne 0){throw 'Native menu exit failed'}
    $stateFile=Join-Path $env:DESKATELIER_DATA 'state.xml'
    [xml]$saved=Get-Content -LiteralPath $stateFile -Raw
    if([int]$saved.SaveData.PausedSeconds -le 0){throw 'Paused timer not persisted'}
    Write-Output 'PASS second EXE launch activated menu quit and retained portable save/reload state'
} finally {
    if(!$process.HasExited){$process.Kill();$process.WaitForExit()}
}
