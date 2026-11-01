# Imlight Example Plugins

Example plugins for the Imlight Plugin API. Each one is a separate project you can build, read through, and use as a starting point for your own plugin.

## The plugins

| Plugin | What it shows |
| --- | --- |
| [ExamplePlugin](ExamplePlugin) | The basics: plugin setup, subscribing to events, registering commands, and working with players and zones. Start here. |
| [BlackjackPlugin](BlackjackPlugin) | Changing player attributes (gold) and building complex commands with subcommands and per-player state. |
| [SetHomePlugin](SetHomePlugin) | Overriding game behaviour by cancelling events, and saving data persistently to the plugin's data folder. |
| [BloodMoonPlugin](BloodMoonPlugin) | Changing creatures at runtime, following duels from start to finish, and running a timed event. |

Each folder has its own README with more detail.

## Requirements

- The .NET 10 SDK
- A copy of Imlight. The projects reference `Imlight.PluginApi` with a relative path (`..\..\..\Imlight\src\Imlight.PluginApi\Imlight.PluginApi.csproj`). If your copy of Imlight is somewhere else, update the `ProjectReference` in each `.csproj`.

## Building and installing

From inside a plugin's folder:

```
dotnet build
```

The compiled plugin will be at `bin/Debug/net10.0/<PluginName>.dll`. Copy it into the `plugins` folder in the same directory as `Imlight.Director.exe`. If that folder doesn't exist, you can create it.

## Creating a new plugin

Each plugin is its own folder with a `.csproj` and a `.sln` side by side. New plugins should follow the same layout.

### Visual Studio

1. **Create a new project → "Class Library"** (C#). Pick the plain one, not "Class Library (.NET Framework)".
2. **Location:** this directory, so the new folder sits next to the other plugins. The Plugin API reference path is relative, so the location matters.
3. **Tick "Place solution and project in the same directory".**
4. **Framework:** .NET 10.0.
5. Delete the generated `Class1.cs`.
6. Add a reference to the Plugin API: right-click the project → **Add → Project Reference → Browse** to `Imlight\src\Imlight.PluginApi\Imlight.PluginApi.csproj`. Then select that reference and set **Copy Local** to **No**. You can also edit the `.csproj` directly (see below).

### Command line

```powershell
dotnet new classlib -n MyPlugin -f net10.0
cd MyPlugin
dotnet new sln -n MyPlugin
dotnet sln add MyPlugin.csproj
dotnet add reference ..\..\..\Imlight\src\Imlight.PluginApi\Imlight.PluginApi.csproj
Remove-Item Class1.cs
```

`dotnet add reference` doesn't add `<Private>false</Private>`, so add that to the `.csproj` yourself.

### The .csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- Point this to your copy of Imlight -->
    <ProjectReference Include="..\..\..\Imlight\src\Imlight.PluginApi\Imlight.PluginApi.csproj">
      <Private>false</Private>
    </ProjectReference>
  </ItemGroup>

</Project>
```

- **`net10.0`** has to match the server's runtime.
- **`<Private>false</Private>`** (Copy Local = No) keeps `Imlight.PluginApi.dll` out of your build output. The server already loads that assembly. If your plugin ships its own copy, the server can end up with two copies of the API types and won't recognise your plugin.
- **`Nullable`** is optional. The example plugins enable it.
- The Class Library template also adds `<ImplicitUsings>enable</ImplicitUsings>`. The example plugins leave it out and write their `using`s explicitly. Keeping it is harmless.

### Minimal plugin

```csharp
using Imlight.PluginApi;

namespace MyPlugin;

public sealed class MyPlugin : ImlightPlugin {
    public override string Name => "MyPlugin";

    public override string Version => "1.0.0";

    public override void OnEnable() {
        // Subscribe to events and register commands here.
    }
}
```

Build it and install it the same way as the examples. See [ExamplePlugin](ExamplePlugin) for how to use events and commands.
