# Entwickeln und Releases veröffentlichen

Dieses Dokument beschreibt die Arbeit an der Windows-App und den ersten Push in `Rakoonir/tafel-email-assistent`.

## Voraussetzungen

- Git und Visual Studio Code
- .NET 10 SDK
- Microsoft Edge WebView2 Runtime
- SSH-Zugriff auf GitHub

Der private SSH-Key bleibt auf dem jeweiligen Computer und darf niemals in dieses Repository. Für einen neuen Computer muss ein SSH-Key lokal eingerichtet und dessen öffentlicher Teil bei GitHub hinterlegt werden.

## Erstes Hochladen dieses Workspace

Das GitHub-Repository enthält bereits einen initialen `README.md`-Commit. Deshalb werden die lokale und entfernte Historie nach dem ersten lokalen Commit einmal zusammengeführt.

1. Im PowerShell-Terminal in den Projektordner wechseln.
2. Vor dem Commit die lokale Konfiguration prüfen:

```powershell
git check-ignore -v desktop/appsettings.json
git status --short
```

`desktop/appsettings.json`, `bin/`, `obj/`, `publish/` und `Releases/` müssen ignoriert sein. Der private SSH-Key liegt außerhalb des Projektordners und wird nicht hinzugefügt.

3. Commit-Autor lokal festlegen. Für die E-Mail kannst du die GitHub-Noreply-Adresse aus den E-Mail-Einstellungen deines GitHub-Kontos verwenden:

```powershell
git config --local user.name "Dein Name"
git config --local user.email "Deine GitHub-E-Mail"
```

4. Das lokale Repository initialisieren und GitHub verbinden:

```powershell
git init -b main
git remote add origin git@github.com:Rakoonir/tafel-email-assistent.git
git fetch origin
git add -A
git status --short
git commit -m "Add Tafel email assistant and updater"
git merge origin/main --allow-unrelated-histories
git push -u origin main
```

Falls `git init` meldet, dass das Repository bereits existiert, nicht erneut initialisieren: zuerst `git status` und `git remote -v` prüfen. Falls der Merge einen Konflikt meldet, nicht blind überschreiben; den Konflikt lösen und anschließend `git add` sowie `git commit` ausführen.

## Entwickeln zuhause

Repository mit SSH klonen:

```powershell
git clone git@github.com:Rakoonir/tafel-email-assistent.git
cd tafel-email-assistent
```

Git-Historie und Projektcode werden geklont, nicht jedoch private App-Einstellungen, Nutzerdaten oder der private SSH-Key. Für lokale Entwicklung die Beispielkonfiguration kopieren:

```powershell
Copy-Item desktop/appsettings.example.json desktop/appsettings.json
```

Die Client-ID und Tenant-ID danach in der App unter **Einstellungen → Microsoft 365** eintragen. Sie werden lokal in `%LOCALAPPDATA%\TafelEmailAssistent\settings.json` gespeichert. Vorlagen und Empfänger liegen separat in der gewählten Datendatei.

Vor der Arbeit und vor dem Push:

```powershell
git pull --rebase origin main
dotnet restore desktop/TafelEmailAssistent.csproj
dotnet build desktop/TafelEmailAssistent.csproj
dotnet run --project desktop/TafelEmailAssistent.csproj
git status --short
```

Änderungen gezielt stagen, prüfen und pushen:

```powershell
git add index.html desktop .github .gitignore ENTWICKLUNG.md
git status --short
git diff --cached
git commit -m "Kurz beschreiben, was geändert wurde"
git push
```

Nie `git add -f desktop/appsettings.json` verwenden. Vor jedem Commit prüfen, dass keine privaten Einstellungen, Tokens, Schlüssel oder Build-Ausgaben gestaged sind.

## Ein Update veröffentlichen

Der GitHub-Actions-Workflow baut ein Release, wenn ein neuer Versions-Tag gepusht wird. Erst den Quellcode auf `main` pushen, dann eine noch unbenutzte Version taggen:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

Die Versionsnummer bei jedem Release erhöhen und einen veröffentlichten Tag nicht wiederverwenden. GitHub Actions erzeugt den x64-Velopack-Installer, Updatepakete und `releases.win.json` und hängt sie an ein GitHub-Release an. Den Lauf unter **Actions** kontrollieren.

Die erste Installation auf einem Computer erfolgt über `TafelEmailAssistent-win-Setup.exe` aus dem Release. Direkt aus einem `publish`-Ordner gestartete Versionen sind nicht Velopack-installiert und können sich nicht automatisch aktualisieren. Nach der Installation prüft die App beim Start auf Updates; zusätzlich kann die Prüfung in den Einstellungen manuell ausgelöst werden.

## Wichtige Dateien

- `desktop/TafelEmailAssistent.csproj`: .NET-Zielversion und Paketabhängigkeiten
- `desktop/Program.cs`: früher Velopack-Einstiegspunkt
- `desktop/MainWindow.xaml.cs`: Microsoft-365- und Update-Logik
- `index.html`: Oberfläche
- `desktop/appsettings.example.json`: nicht geheime Standardkonfiguration für Builds
- `desktop/appsettings.json`: lokale Konfiguration, durch `.gitignore` ausgeschlossen
- `.github/workflows/release.yml`: Build und GitHub-Release bei Versions-Tags
- `%LOCALAPPDATA%\TafelEmailAssistent\settings.json`: lokale Einstellungen je Benutzer
- `Dokumente\Tafel E-Mail-Assistent\tafel-daten.json`: Vorlagen, Personen und Signaturen
