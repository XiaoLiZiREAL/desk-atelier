param([string]$Program=(Join-Path $PSScriptRoot '../build/DeskAtelier.exe'))
$ErrorActionPreference='Stop'
$app=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Program).Path)
$static=[Reflection.BindingFlags]'NonPublic,Static'
$key=$app.GetType('DeskMate.ChinesePlayers').GetMethod('Key',$static)
$choose=$app.GetType('DeskMate.MediaWatcher').GetMethod('Choose',$static)
$preferWeb=$app.GetType('DeskMate.MediaWatcher').GetMethod('PreferNetEaseWeb',$static)
$make=$app.GetType('DeskMate.MediaReading').GetConstructors([Reflection.BindingFlags]'NonPublic,Instance')[0]
$now=[DateTime]::UtcNow
function Sample([string]$id,[string]$channel,[bool]$playing,[string]$line) {
    $sourceKey=$key.Invoke($null,@($id))
    $make.Invoke(@('fixture','artist',$id,$channel,$sourceKey,[TimeSpan]::Zero,[TimeSpan]::Zero,
        $playing,$true,$false,$now,[double]1,'',$line,'',''))
}
function ExpectKey([string]$id,[string]$expected) {
    $actual=$key.Invoke($null,@($id))
    if($actual -ne $expected){throw "Source '$id' classified as '$actual', expected '$expected'"}
}
foreach($id in 'Chrome','MSEdge','Firefox','C:\Program Files\Google\Chrome\Application\chrome.exe',
    'Mozilla.Firefox_n80bbvh6b1yt2!App','308046B0AF4A39CB','Microsoft.MicrosoftEdge.Stable_8wekyb3d8bbwe!App'){
    ExpectKey $id 'browser'
}
foreach($id in 'msedgewebview2.exe','chromedriver.exe','FirefoxHelper.exe','Contoso.ChromePlayer','Spotify.exe'){
    ExpectKey $id 'other'
}
ExpectKey 'NetEase.CloudMusic' 'netease'
ExpectKey 'QQMusic.exe' 'qq'
ExpectKey 'KuGouMusic.exe' 'kugou'
$browser=Sample 'MSEdge' 'smtc' $true ''
$client=Sample 'cloudmusic' 'window' $true 'desktop-only lyric'
$choice=$choose.Invoke($null,@('auto',$browser,$client,$now))
if(![Object]::ReferenceEquals($choice,$browser) -or $choice.LiveLyric){throw 'Client window replaced active browser media or supplied its lyric'}
$paused=Sample 'MSEdge' 'smtc' $false ''
$choice=$choose.Invoke($null,@('auto',$paused,$client,$now))
if(![Object]::ReferenceEquals($choice,$client)){throw 'Paused browser prevented active desktop detection'}

function Track([string]$channel,[string]$title,[string]$artist,[double]$seconds,[double]$duration,
    [string]$appId='Chrome',[bool]$playing=$true,[bool]$known=$true) {
    $make.Invoke(@($title,$artist,$appId,$channel,'browser',[TimeSpan]::FromSeconds($seconds),
        [TimeSpan]::FromSeconds($duration),$playing,$known,($duration -gt 0),$now,[double]1,'','','',''))
}
$web=Track 'netease-web' 'song' '' 44 226
$stream=Track 'smtc' 'live stream' 'streamer' 324 600
$result=$preferWeb.Invoke($null,@($stream,$web))
if(![Object]::ReferenceEquals($result,$web)){throw 'Concurrent livestream leaked its metadata into the active NetEase track'}
$ambiguous=Track 'smtc' 'song' 'wrong artist' 38 237
$result=$preferWeb.Invoke($null,@($ambiguous,$web))
if(![Object]::ReferenceEquals($result,$web)){throw 'Same title with different timings imported an unrelated artist'}
$matching=Track 'smtc' 'song' 'known artist' 44.2 226.4
$result=$preferWeb.Invoke($null,@($matching,$web))
if($result.Artist -ne 'known artist' -or $result.Duration -ne $matching.Duration -or $result.Source -ne 'netease-web'){
    throw "Matching track and clocks failed: artist=$($result.Artist) duration=$($result.Duration.TotalSeconds) source=$($result.Source)"
}
$withArtist=Track 'netease-web' 'song' 'known artist' 0 0
$result=$preferWeb.Invoke($null,@($matching,$withArtist))
if(!$result.HasTimeline -or $result.Duration -ne $matching.Duration){throw 'Matching title and artist lost system timeline'}
$missing=$preferWeb.Invoke($null,@($null,$withArtist))
if(![Object]::ReferenceEquals($missing,$withArtist)){throw 'Missing system session replaced a confirmed web reading'}
foreach($other in @(
    (Track 'smtc' 'song' 'known artist' 47 226 'MSEdge')
    (Track 'smtc' 'song' 'known artist' 47 226 'Chrome' $false)
    (Track 'smtc' 'song' 'known artist' 47 226 'Chrome' $true $false)
    (Track 'smtc' 'song' 'known artist' 47 226 'Unrecognized.exe')
)) {
    $isolated=$preferWeb.Invoke($null,@($other,$withArtist))
    if(![Object]::ReferenceEquals($isolated,$withArtist)){throw 'A paused, unknown or different browser session leaked its timeline'}
}
$instance=[Reflection.BindingFlags]'NonPublic,Instance'
$watcher=[Activator]::CreateInstance($app.GetType('DeskMate.MediaWatcher'))
$site=$watcher.GetType().GetField('netEaseWeb',$instance).GetValue($watcher)
$watcher.Dispose()
$again=$site.GetType().GetMethod('Sample',$instance).Invoke($site,@('chrome'))
if($null -ne $again -or $site.GetType().GetField('busy',$instance).GetValue($site)){
    throw 'Disabling listening left the website probe able to schedule work'
}
Write-Output 'PASS browser source IDs, native clients, independent tab selection, validated browser and live timeline, shutdown'
