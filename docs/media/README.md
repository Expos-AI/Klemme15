# Website clips

The clips on the website, made from the reel's `clean/` clips (run the **reel** workflow, then take them from the
`reel` branch). Each is the app picture only (the 1080 x 1350 area under the caption bar), scaled to 720 x 900,
without sound:

```bash
ffmpeg -i clean/01-quick-test.mp4 -vf "crop=1080:1350:0:290,scale=720:900:flags=lanczos" -an \
  -c:v libx264 -preset slow -crf 27 -pix_fmt yuv420p -movflags +faststart quick-test.mp4
ffmpeg -ss 11.5 -i quick-test.mp4 -frames:v 1 -q:v 4 quick-test.jpg     # the poster: a frame that shows the point
```

Every clip shows a demo car; the page says so under each one ("Demo car", or "Demo car, sped up").
