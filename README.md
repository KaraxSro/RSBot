# RSBot

[![GitHub Issues](https://img.shields.io/github/issues/myildirimofficial/rsbot?label=Open%20Issues)](https://github.com/sdclowen/rsbot/issues)
[![downloads](https://img.shields.io/github/downloads/myildirimofficial/RSBot/total?label=Total%20Downloads)](https://github.com/SDClowen/RSBot/releases)
[![Discord](https://img.shields.io/discord/454345032846016515?label=Discord%20Server)](https://discord.gg/rmd96aus9A)
![GitHub Repo stars](https://img.shields.io/github/stars/myildirimofficial/rsbot)
[![release-latest](https://img.shields.io/github/v/release/SDClowen/RSBot?label=Latest%20Stable)](https://github.com/SDClowen/RSBot/releases/latest)
[![docs](https://img.shields.io/badge/RSBot-Docs-FF00FF)](https://sdclowen.github.io/RSBot)
 
Free, open-source, and highly extensible Silkroad Online botting framework written in C\#. RSBot provides a lightweight clientless/client-attached environment designed for both regular users and developers who want to build custom automation modules.

Feel free to explore the codebase, create pull requests for improvements, or open issues for feature requests and bug reports.

## ✨ Key Features

  * **Advanced Pathfinding:**(Coming Soon) Implements efficient NavMesh-based navigation for accurate coordinate translation and smooth movement without getting stuck.
  * **Packet-Driven Architecture:** Fast, lightweight, and capable of handling complex game packets directly.
  * **Plugin System:** Highly modular design. Write your own C\#, VB.NET, F#, C++/CLR plugins to extend functionality without touching the core engine.
  * **Multi-Client Management:** Seamlessly manage multiple character profiles and configurations.
  * **Comprehensive Client Support:** Supports major global and regional clients (iSRO, TRSRO, vSRO, and various private servers).

## 🚀 Getting Started

### For Users

1.  Download the latest stable executable from the [Releases](https://github.com/myildirimofficial/RSBot/releases/latest) page.
2.  Check the [Documentation & Guides](https://myildirimofficial.github.io/RSBot) to set up your first bot profile.

### For Developers (Building from Source)

**Prerequisites:**

  * [Visual Studio 2026](https://visualstudio.microsoft.com/downloads/)
  * Workloads:
    * `.NET desktop development`
    * `Desktop development with C++`
  * Individual component: `MSVC v143 - VS 2022 C++ x64/x86 build tools (v14.44-17.14)`

**Build Instructions:**

1.  Clone the repository along with its submodules:
    ```bash
    git clone --recursive https://github.com/SDClowen/RSBot.git
    ```
2.  Open the solution in Visual Studio and build (`Ctrl+Shift+B`), **OR** build via command line:
    ```powershell
    dotnet restore
    powershell -ExecutionPolicy Bypass .\build.ps1
    ```
3.  The compiled binaries will be located in the `Build\` directory.

## 🌍 Supported Regions & Versions

The list below represents the collective effort of our community. If your client is not listed, you can help us map the opcodes\!

| Region | Client Version |
| :--- | :--- |
| **Global** | iSRO (International), Rigid (iSRO 2015) |
| **Turkey** | TRSRO |
| **Chinese** | ICCGame, cSRO/-R |
| **Japanese** | JSRO, JSRO\_SL |
| **Korean** | KSRO |
| **Vietnam** | vSRO 188, vSRO 193, vSRO 274, VTC Game |
| **Taiwan** | Digeam, TSRO 110 |
| **Other** | RuSro, Blackrogue 100/110 |

## 🤝 Community & Contributions

RSBot thrives on community contributions. Whether it's updating opcodes for a new game update, fixing a NavMesh routing issue, or writing a new plugin, your help is welcome\!

  * Join the [Discord server](https://discord.gg/rmd96aus9A) for development discussions and support.
  * Check the [Open Issues](https://github.com/myildirimofficial/rsbot/issues) if you want to find something to work on.
  * Support the maintainer by buying a coffee:<br>
    [![Coffee](https://img.shields.io/badge/Donate_@_Buy_Me_A_Coffee-FFDD00?style=for-the-badge&logo=buy-me-a-coffee&logoColor=black)](https://buymeacoffee.com/sdclowen)
    ![GitHub Sponsors](https://img.shields.io/github/sponsors/myildirimofficial?style=for-the-badge)



## ⚖️ License

RSBot is licensed under the **GPLv3 License**. See the `LICENSE` file for more details.

[](https://app.fossa.com/projects/git%2Bgithub.com%2Fmyildirimofficial%2FRSBot?ref=badge_large)
