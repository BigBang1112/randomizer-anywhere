# Randomizer Anywhere

**Randomizer Anywhere** is a successor to [Randomizer TMF](https://github.com/BigBang1112/randomizer-tmf) that brings the Random Map Challenge experience to every TrackMania game (that supports GBXRemote protocol) by spinning up a fully configured dedicated server for you.

Instead of hooking into your local game client, Randomizer Anywhere downloads, configures, and launches a dedicated server, then drives the whole randomizer experience through a few in-game chat commands. This means it works for every player connected to the server, solo or with friends, on your machine or over the network. And on Linux, too!

## Supported games

| Game | Dedicated server |
| --- | --- |
| TrackMania Nations Forever (TMNF) | TMF |
| TrackMania United Forever (TMUF) | TMF |
| TrackMania Nations ESWC (TMN) | TM |
| TrackMania Sunrise eXtreme (TMS) | TM |
| TrackMania Original (TMO) | TM |

## Features

- Automatically downloads, configures, and starts the correct dedicated server for your chosen game
- Random map picking powered by [TMX](https://tm-exchange.com/) (tmnf.exchange, tmuf.exchange, nations/sunrise/original tm-exchange.com)
- All TMX randomization filters supported
- Ingame chat commands to control the session without leaving the game
- Configurable session time limit that freezes/resumes correctly across map loads
- Auto skip on Author/Gold/Silver/Bronze medal, or on finish, once a session is active
- Works solo on LAN, or with multiple players on LAN or the Internet (uses server vote-calling for skip/next map)

## Installation

Randomizer Anywhere is a .NET console app. It is still WIP, so no builds are distributed yet.

1. Install the [.NET SDK](https://dotnet.microsoft.com/download) matching the version required by the project.
2. Clone this repository.
3. Run the app from the `RandomizerAnywhere` project folder:

```
dotnet run --project RandomizerAnywhere
```

Once ready, the server becomes available in your game's "Local network" menu.

## Command line usage

```
RandomizerAnywhere [--game <game>] [--tmx-query <query>] [--no-server] [--help]
```

| Option | Description |
| --- | --- |
| `--game`, `-g <game>` | Game to set up: `TMNF`, `TMUF`, `TMN`, `TMS`, or `TMO`. Falls back to `Game` in `config.toml`, then prompts interactively if not set. |
| `--tmx-query`, `-q <query>` | Raw TMX query string to filter randomized maps, overriding the `TmxQuery` table in `config.toml`. |
| `--no-server` | Skip downloading/starting the dedicated server (useful if you already have one running). |
| `--help`, `-h` | Show usage information. |

## In-game chat commands

| Command | Description |
| --- | --- |
| `/start` | Starts a new randomizer session and queues the first random challenge. |
| `/stop`, `/end` | Stops the active session and resets the current challenge. |
| `/skip` | Skips the current challenge for a new random one. |
| `/imp` | Marks the current challenge as impossible so it won't appear again. |
| `/timelimit`, `/tl <seconds>` | Shows or sets the session time limit (only while no session is active). |
| `/commands` | Lists all available chat commands. |

## Special thanks

- To Flink and Greep, for inventing the challenge
- To the TMX maintainers that make this possible!
