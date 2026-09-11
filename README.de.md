<img src="docs/logo.png" alt="UpdateWatch2" width="96" height="96" />

# UpdateWatch2 Server

**Autor:** Thorsten Schröpel · [🇬🇧 English version](README.md)

[![Docker Image](https://img.shields.io/badge/ghcr.io-vulture20%2Fupdatewatch2--server-2496ED?logo=docker&logoColor=white)](https://github.com/vulture20/updatewatch2-server/pkgs/container/updatewatch2-server)
[![Docker Pulls](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fghcr-badge.elias.eu.org%2Fapi%2Fvulture20%2Fupdatewatch2-server%2Fupdatewatch2-server&query=downloadCount&label=Docker%20Pulls&color=2496ED&logo=docker&logoColor=white)](https://github.com/vulture20/updatewatch2-server/pkgs/container/updatewatch2-server)
[![Docker Image Build](https://github.com/vulture20/updatewatch2-server/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/vulture20/updatewatch2-server/actions/workflows/docker-publish.yml)
[![Status](https://img.shields.io/badge/status-Beta-orange)](#-projektstatus)
[![Lizenz: AGPL v3](https://img.shields.io/badge/license-AGPL--3.0-blue.svg)](LICENSE)

UpdateWatch2 Server ist die selbst gehostete, einzelne Container-Verwaltungszentrale für **UpdateWatch2** — ein System zur zentralen Verteilung, Überwachung und Fernauslösung von Software-/OS-Updates auf Windows- und Linux-Endgeräten. Agents registrieren sich, werden einmal freigegeben und melden von da an ihren Update-Status per gegenseitigem TLS — der Admin entscheidet, wer freigegeben ist, wann installiert wird und wann neu gestartet wird.

> ⚠️ **Beta.** UpdateWatch2 wird aktiv weiterentwickelt. Das zertifikatsbasierte Sicherheitsfundament, das Agent-Onboarding und die Admin-Oberfläche sind implementiert und durch eine automatisierte Testsuite abgedeckt, aber mehrere Teile (echte Windows-Update-Installation, der RPM/dnf-Update-Pfad, das Installations-/Deinstallationsverhalten des Windows-Installers) wurden noch nicht gegen ein echtes Zielsystem verifiziert. Siehe [Projektstatus](#-projektstatus) weiter unten, bevor du dich im Produktivbetrieb darauf verlässt.

Begleit-Repository: [updatewatch2-agent](https://github.com/vulture20/updatewatch2-agent) — der Windows-/Linux-Dienst, den dieser Server verwaltet.

## ✨ Funktionsumfang

### 🔐 Gegenseitige TLS-Sicherheitsbasis
- Eigene, selbst signierte interne Zertifizierungsstelle; jede Anfrage wird sowohl mit Server- als auch mit Client-Zertifikat authentifiziert.
- Ein neuer Agent registriert sich selbst, bleibt aber unbestätigt, bis ein Admin ihn freigibt — einzeln oder in einem Bulk-Vorgang.
- Zertifikate erneuern sich proaktiv vor Ablauf, und ein Admin kann jederzeit ein Zertifikat neu ausstellen (verlorener/zurückgesetzter Agent) oder einen Agent endgültig löschen und ihm damit sofort den Zugriff entziehen.
- Rotation der internen CA-Wurzel mit Übergangsfenster: Ein Agent, dessen Zertifikat noch nicht nachgezogen hat, funktioniert währenddessen weiter. Der Server zeigt genau an, welche Agents vor dem Zurückziehen der alten Wurzel noch erneuern müssen, und ein zurückgebliebener Agent erneuert sich selbst, sobald er es bemerkt, statt seinen normalen Zeitplan abzuwarten.

### 🖥️ Geräteverwaltung
- Agents werden über den Hostnamen identifiziert; Übersichtsliste plus Detailansicht je Agent (Betriebssystem, IP, Version, letzte Meldung, Zertifikatsstatus, ausstehende Updates).
- Einen oder mehrere Agents gleichzeitig freigeben; eine Installation aus der Ferne auslösen; einen ausgemusterten oder versehentlich registrierten Agent endgültig löschen.
- Die Installation von Updates löst nie selbst einen Neustart aus — "Neustart erforderlich" wird immer getrennt gemeldet und entschieden.

### 📦 Update-Verteilung & Agent-Auto-Update
- Echte Windows-Update-API (WUApiLib) sowie Linux-`apt`/`dnf`-Integration für tatsächliche Update-Erkennung und -Installation, kein Platzhalter.
- Der Server prüft selbst auf GitHub nach neuen Agent-Releases, lädt sie einmal herunter und stellt sie den Agents selbst zur Verfügung — Agents brauchen dafür keinen eigenen Internetzugang, und jeder Download wird vor der Anwendung per SHA-256 verifiziert.

### 🔑 Login & Zugriff
- Lokales `admin`-Konto (zufälliges, starkes Passwort beim ersten Start, danach änderbar) sowie optionaler Active-Directory-Login (LDAP-Bind, an Gruppenmitgliedschaft gekoppelt) — beide auf Cookie-Sessions basierend, mit konfigurierbarem Brute-Force-Schutz und einer Ausnahme für vertrauenswürdige IPs.

### 📧 Benachrichtigungen & Administration
- E-Mail-Benachrichtigungen bei einem ODER-Schwellenwert (zu viele Updates auf einer Maschine, oder zu viele betroffene Maschinen), mit Test-Mail-Funktion und einer laufenden Erreichbarkeitswarnung.
- Zweisprachige Admin-Oberfläche (Deutsch/Englisch) mit hellem und dunklem Theme, vollständig responsiv bis hinunter zu Mobilgeräten.
- Nahezu jede Einstellung ist im Admin-Bereich konfigurierbar und wirkt sofort — kein Neustart nötig.

### 🎭 Demo-Modus
- `UPDATEWATCH2_DEMOMODE=true` legt auf einer sonst leeren Instanz ein paar realistische Demo-Agents und -Updates an — idempotent, rein über Umgebungsvariable gesteuert, niemals für den Produktivbetrieb gedacht.

## 🚧 Projektstatus

UpdateWatch2 wurde per **Vibe-Coding** entwickelt: implementiert und iteriert im Dialog mit [Claude Code](https://claude.com/claude-code) (Anthropic), statt Zeile für Zeile von Hand geschrieben, angetrieben von einem menschlich verfassten Architektur-Briefing. Der Code wurde bei jedem Schritt gebaut, getestet und wiederholt live ausgeführt — nicht nur kompiliert — und das zertifikatsbasierte Sicherheitsfundament (Registrierung, Freigabe, Erneuerung, Neuausstellung, Wurzel-Rotation), die Admin-Oberfläche sowie das Agent-Server-Protokoll sind durchgängig implementiert und durch eine automatisierte Testsuite abgedeckt (Server: xUnit; Oberfläche: Vitest). Einige Teile sind ausdrücklich **noch nicht gegen ein echtes Zielsystem live verifiziert** und im Code entsprechend gekennzeichnet: die Windows-Update-API-Integration (WUApiLib-COM), der Linux-`dnf`/`yum`-Update-Pfad (nur `apt` wurde gegen einen echten Paket-Cache verifiziert) sowie das tatsächliche Installations-/Deinstallationsverhalten des NSIS-Windows-Installers über einen Paketmanager. Betrachte dies als eine gut recherchierte, aktiv getestete Implementierung zum Weiterbauen — noch nicht als produktionserprobte Software. Lege regelmäßig Backups der Volumes `/app/data` und `/app/certs` an.

## 🐳 Installation & Konfiguration (Docker)

UpdateWatch2 Server wird als ein einzelnes, in sich geschlossenes Docker-Image ausgeliefert — API und gebaute Admin-Oberfläche in einem Container, kein separater Build-Schritt oder Datenbank-Container nötig:

```bash
docker run -d \
  --name updatewatch2-server \
  -p 8795:8795 \
  -p 8796:8796 \
  -e UPDATEWATCH2_SERVER_HOSTNAME=updatewatch2.example.com \
  -v uw2-data:/app/data \
  -v uw2-certs:/app/certs \
  -v uw2-agent-updates:/app/agent-updates \
  --restart unless-stopped \
  ghcr.io/vulture20/updatewatch2-server:latest
```

Danach **http://localhost:8795** öffnen und als `admin` einloggen — das zufällig erzeugte Passwort des ersten Starts steht im Container-Log (`docker logs updatewatch2-server`); danach über die Oberfläche ändern.

**Ports:** `8795` ist reines HTTP für Admin-Oberfläche und API, gedacht für den Betrieb hinter einem TLS-terminierenden Reverse Proxy. `8796` ist ausschließlich für Agents — Kestrel terminiert dort direkt TLS mit gegenseitiger Zertifikatsauthentifizierung, ohne Proxy davor. `UPDATEWATCH2_SERVER_HOSTNAME` wird zum SAN des auf `8796` präsentierten Zertifikats und **muss** exakt der `ServerAddress` entsprechen, mit der Agents konfiguriert sind — sonst schlägt jede Agent-Verbindung an der Zertifikatsprüfung fehl.

**Volumes — immer mounten, sonst geht beim nächsten Neustart alles verloren:**
- `/app/data` — die SQLite-Datenbank sowie die Data-Protection-Schlüssel, die Admin-Session-Cookies signieren. Verlust bedeutet eine leere Datenbank und alle Admins ausgeloggt.
- `/app/certs` — die interne CA plus das eigene TLS-Zertifikat des Servers. Verlust macht das Zertifikat jedes bereits freigegebenen Agents ungültig.
- `/app/agent-updates` — zwischengespeicherte Downloads des neuesten Agent-Releases, sofern das Agent-Auto-Update aktiv ist. Verlust löst nur einen einmaligen erneuten Download aus, nichts Destruktives.

**Image-Tags:** `:latest` folgt dem jeweils neuesten Push auf `main`; `:v<Version>` (z. B. `:v0.18.0`, passend zur eigenen [`VERSION`](VERSION)-Datei dieses Repos) legt eine konkrete Version fest; `:sha-<Kurz-SHA>` legt einen exakten Commit fest. Images werden von [`docker-publish.yml`](.github/workflows/docker-publish.yml) bei jedem Push auf `main` und bei `v*.*.*`-Tags gebaut und veröffentlicht, abgesichert durch vorher erfolgreiche `dotnet test`- und `npm test`-Läufe — ein Pull Request baut das Image nur, ohne es zu veröffentlichen.

### Wichtige Umgebungsvariablen

| Variable | Erforderlich | Standard | Bedeutung |
|---|---|---|---|
| `UPDATEWATCH2_SERVER_HOSTNAME` | empfohlen | eigener Hostname des Containers | Der von außen erreichbare Hostname, unter dem dieser Server angesprochen wird — siehe Ports oben. Beim Standardwert belassen ist für einen echten Einsatz fast nie richtig. |
| `UPDATEWATCH2_LOGLEVEL` | | `INFO` | `DEBUG`/`INFO`/`WARNING`/`ERROR` — später über die Admin-Oberfläche ohne Neustart änderbar. |
| `UPDATEWATCH2_TRUSTEDIP` | | — | IP oder CIDR-Bereich, der vom Brute-Force-Schutz beim Login ausgenommen ist. |
| `UPDATEWATCH2_AUTOUPDATE` | | (Admin-UI-Schalter, standardmäßig an) | Auf `false` setzen, um die Prüfung auf neue Agent-Releases auf GitHub komplett zu deaktivieren — überschreibt dabei den Admin-UI-Schalter. |
| `UPDATEWATCH2_DEMOMODE` | | — | Auf `true` setzen, um Demo-Agents/-Updates anzulegen. Niemals im Produktivbetrieb setzen. |
| `UPDATEWATCH2_RESET_ADMIN_PASSWORD` | | — | Notfall-Wiederherstellung bei einem ausgesperrten `admin`-Konto: auf ein Passwort setzen, das die Passwortrichtlinie erfüllt (≥16 Zeichen, Groß-/Kleinschreibung, Ziffer, Symbol), um das Admin-Passwort beim nächsten Start zu überschreiben — unabhängig vom aktuellen Passwort. Kann dauerhaft gesetzt bleiben: derselbe Wert wird nur einmal angewendet, ein tatsächlich geänderter Wert setzt erneut zurück. Nach dem Login entfernen oder ändern. |

Siehe [`.env.example`](.env.example) für eine fertige Vorlage zum Kopieren und [`docker/docker-compose.yml`](docker/docker-compose.yml) für ein fertiges lokales Setup (`cd docker && docker compose up --build`).

### Aktualisieren

Einfach das neue Image ziehen und den Container neu anlegen (gleiche `docker run`-Parameter, oder `docker compose up -d --pull always`) — ausstehende EF-Core-Datenbankmigrationen werden beim Start automatisch angewendet.

### Health-Check

Das Image besitzt einen `HEALTHCHECK` (nicht authentifiziertes `GET /api/health`, alle 30s) — `docker ps` zeigt `(healthy)`/`(unhealthy)`, und `docker inspect --format='{{json .State.Health}}' <container>` liefert die Prüfhistorie. Er bestätigt nur, dass der Prozess läuft und antwortet, nicht dass die Datenbank erreichbar ist — ein vorübergehendes SQLite-Problem löst so keine Neustart-Schleife aus.

## 🧱 Technischer Stack

- **Backend:** ASP.NET Core (.NET 10), EF Core + SQLite, Swashbuckle für die OpenAPI/Swagger-Oberfläche. `System.DirectoryServices.Protocols` für plattformübergreifendes LDAP (Active-Directory-Login) — der Server muss dafür nicht selbst unter Windows laufen.
- **Frontend:** React + TypeScript SPA (`web/`), gebaut mit Vite, `react-i18next` für die mitgelieferte deutsch-/englischsprachige Oberfläche.
- **Deployment:** ein Docker-Image (`docker/Dockerfile`) baut `web/` und die .NET-API zu einem einzigen `mcr.microsoft.com/dotnet/aspnet`-Laufzeit-Image zusammen.

## 📁 Verzeichnisstruktur

```
src/UpdateWatch2.Server/   Das API-Projekt — Agents/, Certificates/, Auth/, Admin/, AgentUpdates/, Api/Controllers/, Db/
tests/                     xUnit-Unit- sowie WebApplicationFactory-basierte Integrationstests
web/                       Die Admin-Oberfläche — ein eigenständiges npm-Paket, nicht Teil des .NET-Builds
docker/                    Dockerfile, docker-compose.yml
config/, docs/             Deployment-Gerüst, Logo dieser README
certs/, data/, logs/, agent-updates/   Nur zur Laufzeit, gitignored — niemals committen
```

## 🛠️ Lokale Entwicklung

Erfordert das .NET-10-SDK und Node 22.

```bash
# API — standardmäßig http://localhost:8795
dotnet build
dotnet test
dotnet run --project src/UpdateWatch2.Server   # SQLite-DB wird unter server/data/updatewatch2.sqlite angelegt/migriert

# Admin-Oberfläche — http://localhost:5173
cd web
npm install
cp .env.example .env.local   # VITE_API_BASE_URL auf einen laufenden Server zeigen lassen
npm run dev
```

Frontend-Build / Typprüfung / Tests:

```bash
cd web
npm run build   # tsc -b Typprüfung, dann Produktions-Build nach dist/
npm test        # vitest run
```

Das Docker-Image lokal bauen, statt von `ghcr.io` zu ziehen: `docker build -f docker/Dockerfile -t updatewatch2-server .` (Build-Kontext muss das Repo-Wurzelverzeichnis sein, nicht `docker/`).

## 📜 Änderungsprotokoll

Siehe [`CHANGELOG.md`](CHANGELOG.md) (Englisch) für eine versionsweise Historie aller nennenswerten Änderungen.

## ⚖️ Lizenz

Copyright (C) 2026 Thorsten Schröpel.

UpdateWatch2 Server ist freie Software: Du darfst sie unter den Bedingungen der [GNU Affero General Public License](LICENSE), wie von der Free Software Foundation veröffentlicht, weitergeben und/oder verändern, entweder gemäß Version 3 der Lizenz oder (nach deiner Wahl) jeder späteren Version. Siehe [LICENSE](LICENSE) oder <https://www.gnu.org/licenses/agpl-3.0.html> für den vollständigen Text.

Kurz gefasst: Du darfst UpdateWatch2 frei betreiben, verändern und selbst hosten. Die zusätzliche Pflicht, die AGPL im Vergleich zu einer gewöhnlichen GPL-Lizenz mit sich bringt: Wenn du eine **veränderte** Version betreibst und anderen Nutzern über ein Netzwerk zugänglich machst, musst du diesen Nutzern auch Zugriff auf deinen veränderten Quellcode geben — nicht nur Personen, denen du die Software direkt aushändigst. Der reine Betrieb einer unveränderten Kopie für dich selbst bringt über die üblichen Copyleft-Bedingungen hinaus keine zusätzliche Pflicht mit sich.
