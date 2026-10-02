# Security

## What the application accesses

G HUB Battery Widget talks only to `127.0.0.1:9010`, the local WebSocket listener exposed by Logitech G HUB. It sends two read-only `GET` messages: one to enumerate devices and one to read the selected device's battery state.

The application does not:

- connect to remote hosts;
- upload device information;
- read browser data, documents, credentials or clipboard contents;
- install a service or driver;
- request elevation;
- execute commands received over the WebSocket;
- load third-party DLLs or packages.

The Win32 calls in the source are limited to changing the widget's click-through window style and releasing a dynamically created tray-icon handle.

## Local files and registry

`GHubBatteryWidget.ini` contains only the widget's physical X/Y screen position, background opacity, always-on-top state and click-through state. It is intentionally excluded from version control and is never included in release archives.

If the user explicitly enables **Start with Windows**, the application writes its own quoted executable path to:

`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\GHubBatteryWidget`

Disabling the option removes that value.

## Defensive limits

- WebSocket connections and reads use a six-second timeout.
- A received message is rejected above 1 MiB.
- Device IDs must match a restricted alphanumeric pattern.
- Battery percentages are clamped to `0..100`.
- Only a single application instance is allowed per Windows session.

## Known trust boundary

G HUB's local API does not offer application-level authentication. The widget therefore assumes that the service listening on `127.0.0.1:9010` is Logitech G HUB. A malicious process already executing in the same local environment could attempt to impersonate that endpoint. The limits above reduce parsing and resource-exhaustion risk, but they cannot add authentication to the upstream API.

## Reporting a vulnerability

Please avoid including personal identifiers, device serial numbers or logs containing them in a public issue. Open an issue with a minimal reproduction, or use GitHub's private vulnerability reporting feature if it is enabled for the repository.
