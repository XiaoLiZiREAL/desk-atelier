param([string]$Program=(Join-Path $PSScriptRoot '../build/DeskAtelier.exe'))
$ErrorActionPreference='Stop'
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Program).Path)
$static=[Reflection.BindingFlags]'NonPublic,Static'
$players=$assembly.GetType('DeskMate.ChinesePlayers')
$key=$players.GetMethod('Key',$static)
$display=$players.GetMethod('DisplayName',$static)
$owner=$players.GetMethod('IsProcess',$static)
$parse=$players.GetMethod('Parse',$static)
$advance=$players.GetMethod('WithObservedProgress',$static)
$label=$assembly.GetType('DeskMate.PlayerLabel')
$listType=[Collections.Generic.List[int]].GetGenericTypeDefinition().MakeGenericType($label)
$labelCtor=$label.GetConstructors()[0]
$readingCtor=$assembly.GetType('DeskMate.MediaReading').GetConstructors([Reflection.BindingFlags]'NonPublic,Instance')[0]
function ExpectKey([string]$id,[string]$expect) {
    $actual=$key.Invoke($null,@($id))
    if($actual -ne $expect){throw "Wrong source for '$id' ($actual, expected $expect)"}
}
foreach($case in @(
    @('netease','cloudmusic','网易云音乐'),@('kugou','KuGou','酷狗音乐'),@('qq','QQMusic','QQ音乐'),
    @('soda','SodaMusic','汽水音乐'),@('kuwo','KwMusic','酷我音乐'),@('migu','MiguMusic','咪咕音乐'),
    @('lx','lx-music-desktop','洛雪音乐助手'),@('yesplay','YesPlayMusic','YesPlayMusic')
)) {
    $name,$exe,$brand=$case
    ExpectKey $exe $name
    ExpectKey "C:\Program Files\Player\$exe.exe" $name
    ExpectKey $brand $name
    if($display.Invoke($null,@($name)) -ne $brand -or !$owner.Invoke($null,@($name,$exe))){throw "Source mapping failed for $name"}
    ExpectKey ("${exe}Updater.exe") 'other'
}
ExpectKey 'electron.app.SodaMusic' 'soda'
ExpectKey '1F8B0F94.122165AE053F_j2p0p5q0044a6!App' 'netease'
ExpectKey 'cn.toside.music.desktop' 'lx'
foreach($bad in 'SodaMusicLauncher.exe','C:\Apps\SodaMusic\Helper.exe','Contoso.QQMusic','https://example.test/KuGou.exe','https://fake.test/chrome.exe') {ExpectKey $bad 'other'}
if($owner.Invoke($null,@('soda','SodaMusicLauncher')) -or $owner.Invoke($null,@('soda','MiguMusic'))){throw 'Foreign process accepted for lyric window capture'}

function AddLabel($labels,[string]$id,[string]$value,[string]$kind,[string]$context) {
    $labels.Add($labelCtor.Invoke(@($id,$value,$kind,$context,1,$true)))
}
function ReadLabels($labels) {$parse.Invoke($null,@('soda','SodaMusic','搜索 - 歌单',$labels,$false))}
$labels=[Activator]::CreateInstance($listType)
AddLabel $labels 'songName' '歌名：推荐栏位' 'Text' 'recommendation'
AddLabel $labels 'artistName' '歌手：推荐作者' 'Text' 'recommendation'
AddLabel $labels 'btnPlay' '播放' 'Button' 'recommendation'
AddLabel $labels 'duration' '00:02 / 05:00' 'Text' 'recommendation'
$read=ReadLabels $labels
if($read.Title -or $read.Artist -or $read.Playing -or $read.StateKnown -or $read.HasTimeline){throw 'Idle catalog mistaken for current playback'}
AddLabel $labels 'title' '当前作品' 'Text' 'playerBar'
AddLabel $labels 'artist' '当前艺术家' 'Text' 'playerBar'
AddLabel $labels 'timeline' '00:12 / 03:00' 'Text' 'playerBar'
AddLabel $labels 'activeLyric' '窗边落下一点光' 'Text' 'playerBar'
$read=ReadLabels $labels
if($read.Title -ne '当前作品' -or $read.Artist -ne '当前艺术家' -or $read.Position.TotalSeconds -ne 12 -or
    $read.LiveLyric -ne '窗边落下一点光' -or !$read.HasTimeline -or $read.StateKnown){throw 'Current song metadata or unknown playback state lost'}

function Prior([string]$app,[int]$position,[int]$age,[string]$artist='当前艺术家') {
    $readingCtor.Invoke(@($read.Title,$artist,$app,'window','soda',[TimeSpan]::FromSeconds($position),$read.Duration,
        $false,$false,$true,$read.SampledUtc.AddSeconds(-$age),[double]1,'','','',''))
}
$moving=$advance.Invoke($null,@($read,(Prior 'SodaMusic' 10 2)))
if(!$moving.StateKnown -or !$moving.Playing){throw 'Two consistent samples failed to detect real progress'}
foreach($prior in @((Prior 'KwMusic' 10 2),(Prior 'SodaMusic' 12 2),(Prior 'SodaMusic' 1 11),(Prior 'SodaMusic' 10 2 '其他歌手'))) {
    if($advance.Invoke($null,@($read,$prior)).Playing){throw 'Unrelated, frozen, or stale progress treated as playback'}
}
AddLabel $labels 'playPause' '播放' 'Button' 'playerBar'
$paused=ReadLabels $labels
if(!$paused.StateKnown -or $paused.Playing -or $advance.Invoke($null,@($paused,(Prior 'SodaMusic' 10 2))).Playing){throw 'Explicit pause overridden'}
$sodaField=$players.GetMethod('SodaField',$static)
$bar=[Activator]::CreateInstance($listType)
foreach($field in @(
    @('title disabled','Hyperlink','曲目甲','bottom-player',''),@('vip-badge','Text','VIP','bottom-player','title'),
    @('desc','Text','演唱乙','bottom-player',''),@('indicator','Text','01:03 / 03:12','bottom-player',''),
    @('title','Text','推荐栏位','recommendation','')
)){
    $css,$kind,$value,$context,$inherited=$field
    $item=$labelCtor.Invoke(@('',$value,$kind,$context,1,$true))
    $item.TrackField=$sodaField.Invoke($null,@($css,$context,$inherited))
    $bar.Add($item)
}
$matched=ReadLabels $bar
if($matched.Title -ne '曲目甲' -or $matched.Artist -ne '演唱乙' -or $matched.Position.TotalSeconds -ne 63 -or $matched.StateKnown){
    throw 'Soda 3.7 playback bar did not isolate the song title, artist and clock'
}
Write-Output 'PASS eight native player identities, ownership, transport-only metadata, lyrics, pause and observed progress'
