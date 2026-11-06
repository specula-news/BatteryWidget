# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

Until signing is active, releases are published unsigned. Every release is built automatically from this repository by
GitHub Actions ([workflow](.github/workflows/release.yml)); each signed release is approved by hand before it is signed.

## Team roles

- Authors (committers): [specula-news](https://github.com/specula-news)
- Reviewers: [specula-news](https://github.com/specula-news) – changes proposed by anyone else (pull requests) are reviewed before they are merged
- Approvers: [specula-news](https://github.com/specula-news) – approves each signing request

All team members use multi-factor authentication for GitHub and SignPath.

## Privacy policy

Battery Widget does not collect personal data and has no telemetry, accounts or analytics. Settings are stored only on
your PC, next to the program file.

The program connects to the following services. Each one is listed with what it receives and how to turn it off.

| When | Service | What is sent | How to turn it off |
|---|---|---|---|
| At start, to look for a new version | GitHub API (api.github.com) for this repository's releases | Your IP address and a standard web request | Right-click → *Updates* → untick *Check quietly at every start* |
| When you press *Update now* | GitHub (github.com) | Your IP address; the new version is downloaded | Don't press *Update now* |
| Weather widget, every 15–60 minutes | [Open-Meteo](https://open-meteo.com) | The latitude and longitude of the chosen place | Switch the weather widget off (*Widgets → Weather*) |
| Weather widget, automatic location | Windows' location service; if unavailable, ipwho.is | Your IP address (ipwho.is) to find your approximate location | Right-click the weather → *Location → Set location…* (no IP lookup), or switch the weather widget off |
| When you search for a place | Open-Meteo geocoding | The text you type | Don't search; or switch the weather widget off |
| Only when you click *Install PawnIO…* | GitHub (namazso/PawnIO.Setup releases) | Your IP address; the official installer is downloaded and checksum-checked | Don't click it |
| Only when you click a device | The maker's app or web page you chose (e.g. Keychron Launcher) | Whatever that app or web page receives | Don't click devices |

Everything else – battery levels, sensor values, Logitech G HUB (a local connection on your own PC) – stays on your computer.

The privacy policies of these services apply to what they receive:
[GitHub](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement) ·
[Open-Meteo](https://open-meteo.com/en/terms) · ipwho.is.

## Third-party components

- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) 0.9.6 (MPL-2.0), included unmodified and checksum-checked at build time, for CPU/GPU/motherboard sensors.
- [PawnIO](https://github.com/namazso/PawnIO.Setup) – not included; the official signed installer is downloaded only if you ask for it.

## Uninstall

Battery Widget is portable and installs nothing. To remove it: right-click → *Start with Windows* off (if it was on),
*Quit*, then delete the program folder.
