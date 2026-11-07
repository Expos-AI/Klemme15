# Promo reel

Records the real app with the demo cars, one scene per feature, and turns the recordings into vertical clips
(1080 x 1920) for TikTok, Reels and Shorts: the camera follows the part of the screen that matters, a cursor travels
to each button and clicks, and a caption bar says what the scene shows. Nothing is staged: every scene is the app
doing what it does, against a demo car.

Run the **reel** workflow from the Actions tab (optionally with a comma-separated list of scene ids). The result is
pushed to the `reel` branch:

- `reel.mp4`: every captioned clip in a row
- `captioned/`: one clip per feature, with caption bars
- `labeled/`: the same clips with only the small print ("demo car shown") in a pill at the top, for videos where you do the talking
- `clean/`: the same clips without text, for your own captions
- `timelines.txt`: what each clip shows from which second, to time a voiceover or a script against
- `landscape/`: the raw recordings, 1920 wide
- `stills/`: one frame of each clip

Locally (Linux, with ffmpeg and Pillow): `dotnet run --project tools/Klemme15.Reel -- raw` then
`python3 tools/Klemme15.Reel/compose.py raw reel`.

Scenes live in `Program.cs`: each one drives the view models, says where the camera should look (`Focus`,
`Overview`) and where the cursor clicks (`Click`), and notes what's on screen from each moment (`Mark`, which feeds `timelines.txt`). `compose.py` does the rest.
