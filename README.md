# Live TV Images (TMDB/TVDB) for Emby

![Plugin icon](src/Emby.EpgImages/thumb.png)

Adds missing movie and series images to the Emby Live TV guide using the installed **TheMovieDb** and **TheTVDB** providers.

**Public beta:** verified on Emby Server **4.10.0.40** with .NET 8. Compatibility with other versions has not yet been verified. This project is independent of Emby, TMDB and TheTVDB.

## Features

- Uses existing Emby provider plugins; no additional API key is required.
- Searches by series name rather than episode title, with German metadata lookup.
- Accepts unambiguous normalized title matches and checks movie years when available.
- Preserves existing programme images.
- Runs in batches every five minutes and optionally after guide refresh.
- Reuses title matches across episodes and repeated broadcasts.
- Reports pending titles and maintains a bounded match cache.
- Provides an English settings page, plugin icon and scheduled task.

## Requirements

- Emby Server 4.10 on a .NET 8 runtime; tested with 4.10.0.40.
- Working Live TV channels and guide data.
- TheMovieDb plugin installed.
- TheTVDB plugin installed if the optional series fallback is enabled.

M3U TV Tuner and XMLTV remain necessary when they provide your Live TV source. This plugin only adds images; it does not replace a tuner or an EPG provider.

## Installation

1. Download `Emby.EpgImages.dll` from a release.
2. Copy it into the Emby Server plugins folder.
3. Restart Emby when no recordings or important playback sessions are running.
4. Open Server Settings > Plugins > Live TV Images (TMDB/TVDB) and enable it.
5. Set the batch size, enable the TVDB fallback if desired, and save.
6. Run Scheduled Tasks > Live TV > Update Live TV Images once.

The default for a new installation is 100 new title searches per run. Existing installations retain their configured limit. Confirm that the task has a five-minute interval trigger; Emby may retain an existing task's previous trigger configuration when upgrading.

## How batches work

The limit counts **new title searches**, not programme entries or image downloads. A cached series match can provide images for all corresponding episodes without consuming the search budget. Every subsequent run continues the titles that have not yet been checked. A negative result is retried after one day, a positive match after 30 days.

## Cache and cleanup

The plugin stores title matches and remote image URLs, without downloading a separate image copy per programme. Unused entries expire after seven days and the match cache is capped at 5,000 titles. For finished programmes, references to TMDB/TVDB images are cleared on the next run.

Emby's own Cache file cleanup task manages its shared image and thumbnail cache. The plugin does not remove channel logos, recordings or library artwork.

## Known limitations

- Sports and news are excluded.
- Exact title matching intentionally leaves ambiguous or unmatched titles without images.
- Incorrect movie/series flags in EPG data can prevent matching.
- German metadata lookup is currently fixed rather than configurable.
- Other server versions and runtimes still need community testing.

## Build

Install the .NET 8 SDK, then run:

```sh
dotnet restore src/Emby.EpgImages/Emby.EpgImages.csproj
dotnet build src/Emby.EpgImages/Emby.EpgImages.csproj -c Release --no-restore
```

The build uses the pinned official Emby SDK NuGet package. Server libraries are not included in release artifacts. GitHub Actions builds pushes and pull requests; tags starting with `v` create prereleases.

## Reporting problems

Open a GitHub issue with your Emby version, runtime, guide source, example programme title and the task result. Remove tokens, passwords, private server addresses and personal data from logs before posting.

## License and credits

Plugin source: MIT license. Images and metadata remain subject to the terms of their respective providers.

Images and metadata: [TMDB](https://www.themoviedb.org) and [TheTVDB](https://thetvdb.com).
This product uses the TMDB API but is not endorsed or certified by TMDB.

Emby plugin documentation: https://dev.emby.media/
