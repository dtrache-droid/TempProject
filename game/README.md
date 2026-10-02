# The Last Letter West

An offline wagon-trail story game for Android (and any browser). One file, no internet, no ads.

## Play on Android
1. Copy **`index.html`** to the phone (download it, send it to yourself, or USB).
2. Open it with **Chrome** from the Files / Downloads app. It runs fully offline.
3. For a fullscreen "app" feel, host the whole `game/` folder on any https site (e.g. GitHub Pages),
   open it once in Chrome, then menu → **Add to Home screen**. The service worker (`sw.js`) caches it for offline play.

## How to play
- The wagon moves by itself. Set **Pace** and **Rations**; use **Hunt**, **Rest** and **Journal**.
- Reach each landmark: story scenes ask you to choose. Choices change morale, supplies, who lives, and which ending you get.
- First frost (day 175) is the deadline. Resting and detours cost days.
- Progress autosaves at landmarks.

## Files
`index.html` (the game) · `manifest.json`, `sw.js`, `icon-*.png` (optional home-screen install)
