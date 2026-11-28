"""Assert the retired chair is absent and desktop/window animation routes are independent."""
from pathlib import Path
repo=Path(__file__).resolve().parents[1]
src=repo/'src'
assets=src/'assets'
assert not list(assets.glob('perch-*.png'))
assert not (assets/'window-perch-walnut.png').exists()
window=(src/'WindowPerch.cs').read_text()
desk=(src/'PetLife.cs').read_text()
menu=(src/'PetWindow.cs').read_text()
assert 'PerchMode.Scene' not in window and 'PerchMode.Edge' in window
assert 'TryStartDesktopScene' in desk and 'LoadWindowPerchPoses(currentArtId,PerchMode.Scene)' in desk
assert 'perchTarget!=IntPtr.Zero || CompanionOccupied()' in desk
assert '窗口动作' not in window+menu and '胡桃木椅' not in window+menu
assert 'LeavePerchText' in menu
print('Retired chair and selector absent; independent runtime routes verified')
