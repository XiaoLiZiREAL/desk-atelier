param([string]$Program=(Join-Path $PSScriptRoot '../build/DeskAtelier.exe'))
$ErrorActionPreference='Stop'
$app=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Program).Path)
$static=[Reflection.BindingFlags]'NonPublic,Static'
$key=$app.GetType('DeskMate.ChinesePlayers').GetMethod('Key',$static)
$choose=$app.GetType('DeskMate.MediaWatcher').GetMethod('Choose',$static)
$preferWeb=$app.GetType('DeskMate.MediaWatcher').GetMethod('PreferWebPlayer',$static)
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
$pausedClient=Sample 'cloudmusic' 'smtc' $false ''
$choice=$choose.Invoke($null,@('auto',$pausedClient,$client,$now))
if($choice.Playing -or $choice.LiveLyric){throw 'A cached player window overrode a system pause'}
$audioChoice=$app.GetType('DeskMate.MediaWatcher').GetMethod('ChooseAudio',$static)
$sodaOutput=$make.Invoke(@('','','SodaMusic.exe','player-audio','soda',[TimeSpan]::Zero,[TimeSpan]::Zero,
    $true,$true,$false,$now,[double]1,'','','',''))
if($audioChoice.Invoke($null,@($pausedClient,$sodaOutput)).PlayerKey -ne 'soda'){throw 'A paused client hid another audible player'}
$sodaPause=Sample 'SodaMusic.exe' 'smtc' $false ''
if($audioChoice.Invoke($null,@($sodaPause,$sodaOutput)).Playing){throw 'Output tail overrode a system pause'}
if($audioChoice.Invoke($null,@($client,$sodaOutput)).PlayerKey -ne 'netease'){throw 'Output-only reading replaced identified playing track'}
function WindowTrack([string]$key,[string]$id,[DateTime]$when,[string]$title='current track') {
    $make.Invoke(@($title,'singer',$id,'window',$key,[TimeSpan]::FromSeconds(5),[TimeSpan]::FromSeconds(90),
        $false,$false,$true,$when,[double]1,'','current lyric','accessibility',''))
}
$nowPlaying=$audioChoice.Invoke($null,@((WindowTrack 'soda' 'SodaMusic' $now),$sodaOutput))
if($nowPlaying.Title -ne 'current track' -or !$nowPlaying.Playing -or $nowPlaying.HasTimeline -or $nowPlaying.LiveLyric -ne 'current lyric'){
    throw 'Output should corroborate the current native window and its live lyric without inventing a clock'
}
$lyricOnly=$audioChoice.Invoke($null,@((WindowTrack 'soda' 'SodaMusic' $now ''),$sodaOutput))
if($lyricOnly.Title -or $lyricOnly.LiveLyric -ne 'current lyric' -or $lyricOnly.LyricSource -ne 'accessibility'){throw 'Lost a lyric-only desktop window'}
$partialSession=$make.Invoke(@('system track','system artist','SodaMusic.exe','smtc','soda',[TimeSpan]::FromSeconds(6),
    [TimeSpan]::FromSeconds(120),$false,$false,$true,$now,[double]1,'','','',''))
$confirmed=$audioChoice.Invoke($null,@($partialSession,$sodaOutput))
if($confirmed.Title -ne 'system track' -or $confirmed.Source -ne 'smtc' -or !$confirmed.Playing -or $confirmed.HasTimeline){throw 'Lost track from a system session without transport state'}
foreach($uncorroborated in @((WindowTrack 'soda' 'SodaMusic' $now.AddSeconds(-8)),(WindowTrack 'migu' 'MiguMusic' $now))) {
    if($audioChoice.Invoke($null,@($uncorroborated,$sodaOutput)).Title){throw 'A stale window or a different client leaked its title'}
}

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
$excerpt=Track 'soda-web' 'song' 'known artist' 0 0
$preview=$preferWeb.Invoke($null,@($matching,$excerpt))
if(![Object]::ReferenceEquals($preview,$excerpt) -or $preview.HasTimeline){throw 'A Soda excerpt inherited a full-song clock'}
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
