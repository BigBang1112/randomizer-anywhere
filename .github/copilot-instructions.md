## Documentation

When making a change that affects user-facing behavior, update [README.md](../README.md) in the same change so it stays accurate. This includes, but is not limited to:

- Adding, removing, or changing a command line option (`RandomizerAnywhere/Config/CmdConfig.cs`)
- Adding, removing, or changing an environment variable (`RANDANY_*`, read via `RandomizerAnywhere/Config/Configurator.cs`)
- Adding, removing, or changing an in-game chat command (`RandomizerAnywhere/RandomizerGame.cs`, registered in `RandomizerAnywhere/RandomizerSetup.cs`)
- Adding, removing, or renaming a bundled preset (`RandomizerAnywhere/Presets/*.toml`)
- Changing configuration precedence or `config.toml` structure (`RandomizerAnywhere/Config/GlobalConfig.cs`, `RandomizerAnywhere/Config/PresetConfig.cs`)
- Adding or removing supported games or dedicated servers

Keep README tables (command line options, environment variables, presets, chat commands) consistent with the actual code/config — don't guess values, verify them by reading the relevant source or `.toml` files first.
