# Tafel E-Mail-Assistent für Windows

Die Desktop-App bettet die bestehende Website ein und erstellt formatierte E-Mail-Entwürfe in Microsoft 365 über Microsoft Graph. Sie sendet nichts automatisch; jeder Entwurf wird vor dem Versand in Outlook geöffnet.

## Einmalige Einrichtung

1. In Microsoft Entra eine App-Registrierung für **Konten in diesem Organisationsverzeichnis** anlegen.
2. Unter **Authentifizierung** eine Plattform **Mobilgerät und Desktopanwendungen** hinzufügen und die Umleitungs-URI `http://localhost` aktivieren. Öffentliche Clientflows müssen zugelassen sein.
3. Unter **API-Berechtigungen** die delegierte Microsoft-Graph-Berechtigung `Mail.ReadWrite` hinzufügen. Falls eure Organisation es verlangt, muss die IT die Zustimmung erteilen.
4. Für die lokale Entwicklung `appsettings.example.json` nach `appsettings.json` kopieren und die App starten. Client-ID und Tenant-ID anschließend im Menü **Einstellungen → Microsoft 365** eintragen. Es wird kein Client-Secret benötigt.

## Start und Veröffentlichung

Im Projektordner:

```powershell
dotnet run --project TafelEmailAssistent.csproj
dotnet publish TafelEmailAssistent.csproj -c Release -r win-x64 --self-contained true
```

Die veröffentlichte Anwendung benötigt die Microsoft Edge WebView2-Laufzeit. Beim ersten Erstellen eines Entwurfs meldest du dich im Browser bei Microsoft 365 an. Der Entwurf wird im Postfach gespeichert und über Outlook im Browser geöffnet.

## Auto-Updates über GitHub

Der Updater verwendet das öffentliche Repository `Rakoonir/tafel-email-assistent`. Er prüft beim Start nach einer neuen Version; zusätzlich kannst du die Prüfung in den **Einstellungen** auslösen. Vor Download und Neustart fragt die App nach Zustimmung.

Die GitHub-Actions-Pipeline `.github/workflows/release.yml` baut einen Velopack-Installer, ein MSI für die Windows-App-Liste und Updatepakete, sobald ein Versions-Tag wie `v1.0.2` gepusht wird:

```powershell
git tag v1.0.2
git push origin v1.0.2
```

Für die Installation mit einem Eintrag in **Installierte Apps** `TafelEmailAssistent-win.msi` aus dem GitHub-Release verwenden. Es wird pro Benutzer installiert und benötigt keine Administratorrechte. `TafelEmailAssistent-win-Setup.exe` bleibt zusätzlich als Velopack-Ein-Klick-Installer verfügbar. Als Paket-Hersteller wird **Kjano Eglins** angezeigt; für eine vertrauenswürdige Herausgeberwarnung braucht es zusätzlich eine Codesignatur. Eine bisher manuell aus einem `publish`-Ordner gestartete Kopie kann sich nicht selbst aktualisieren und muss einmalig über einen Installer ersetzt werden.

`appsettings.json` ist lokal und wird durch `.gitignore` vom Repository ausgeschlossen. GitHub Actions paketiert `appsettings.example.json` mit leeren Microsoft-365- und Support-Feldern. Die IDs und Support-Kontakte werden je Installation im Einstellungsmenü gepflegt und unter `%LOCALAPPDATA%\TafelEmailAssistent\settings.json` gespeichert. Client-ID und Tenant-ID sind keine Geheimnisse; ein Client-Secret oder GitHub-Token darf nicht in die App eingebettet werden.

Unter **Prüfen & Senden** kannst du mehrere Absender-Signaturen anlegen und für den jeweiligen Versandlauf auswählen. Signatur-HTML lässt sich aus einer bestehenden Mail einfügen oder über **HTML-Code** bearbeiten. Unterstützt werden gängige Inline-Formatierung, Tabellen, Links und HTTPS- beziehungsweise eingebettete Rasterbilder. Skripte und unsichere Inhalte werden entfernt. Support-Kontakte lassen sich nach der Installation im Einstellungsmenü bearbeiten.

Vorlagen, Empfänger, Signaturen und Design werden in `Dokumente\Tafel E-Mail-Assistent\tafel-daten.json` gespeichert. Auf diesem Rechner liegt die Datei im synchronisierten Dokumente-Ordner von OneDrive. Beim ersten Start werden vorhandene App-Daten aus dem WebView2-Profil übernommen. Die JSON-Datei kann bei geschlossener App gesichert werden.

Im Menü **Einstellungen** kannst du den Datenordner, die Standard-Uhrzeit und die Support-Kontaktdaten ändern. Neue Terminzeiten starten standardmäßig um 08:00 Uhr und sind auf 15-Minuten-Schritte begrenzt. Benutzereinstellungen werden separat unter `%LOCALAPPDATA%\TafelEmailAssistent\settings.json` gesichert. Wenn der ausgewählte Datenordner bereits eine `tafel-daten.json` enthält, fragt die App vor dem Laden dieser Daten nach.

Die Daten aus der bisherigen Browser-Version bleiben dort gespeichert, werden aber nicht automatisch in das separate WebView2-Profil der Desktop-App übertragen.