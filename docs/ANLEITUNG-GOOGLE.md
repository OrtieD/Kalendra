# Google-Kalender in Kalendra verbinden

## Der einfache Weg: per Adresse (nur anzeigen, 2 Minuten)
1. Im Browser **calendar.google.com** öffnen.
2. Links unter „Meine Kalender“ mit der Maus über deinen Kalender fahren → **⋮** → **Einstellungen und Freigabe**.
3. Ganz nach unten scrollen zu **„Privatadresse im iCal-Format“** → Kopieren-Symbol.
4. In Kalendra links unten **Konto verbinden …** (bzw. **＋ Konto hinzufügen …**) →
   **Google-Kalender per Adresse (einfach)** → Adresse einfügen → **Hinzufügen**.
5. Für weitere Google-Kalender (z. B. Familie) Schritt 2 bis 4 wiederholen.

Kalendra holt Änderungen alle 5 Minuten (Google selbst aktualisiert diese Adresse manchmal
erst nach einigen Stunden). Termine bearbeitest du auf diesem Weg weiter in Google oder am Mac.
Die Adresse ist wie ein Passwort, also nicht weitergeben.

---

## Der volle Weg: Google-Konto (bearbeiten in beide Richtungen)

Google erlaubt Kalender-Programmen den Zugriff nur über eine eigene „App-Kennung“ (OAuth-Client).
Die legst du einmalig kostenlos in der Google Cloud Console an. Dauert etwa 10 Minuten.

### 1. Projekt und Schnittstelle
1. https://console.cloud.google.com öffnen, mit deinem Gmail-Konto anmelden.
2. Oben „Projekt auswählen“ → **Neues Projekt**, Name z. B. `Kalendra` → Erstellen.
3. Links **APIs & Dienste → Bibliothek**, nach **CalDAV API** suchen → **Aktivieren**.
   (Wichtig: die *CalDAV API*, nicht nur die „Google Calendar API“.)

### 2. Zustimmungsbildschirm (OAuth consent screen / „Google Auth Platform“)
1. **APIs & Dienste → OAuth-Zustimmungsbildschirm** → Benutzertyp **Extern**.
2. App-Name `Kalendra`, deine Gmail-Adresse als Support- und Kontakt-Adresse → Speichern.
3. Unter **Zielgruppe / Testnutzer** deine Gmail-Adresse hinzufügen.
4. Empfehlung: danach **App veröffentlichen** („In Produktion“).
   Im Testmodus läuft die Anmeldung bei Google nach 7 Tagen ab und Kalendra fragt dann erneut.
   Eine Prüfung durch Google ist für den Eigengebrauch nicht nötig.

### 3. Zugangsdaten
1. **APIs & Dienste → Anmeldedaten → Anmeldedaten erstellen → OAuth-Client-ID**.
2. Anwendungstyp **Desktop-App**, Name `Kalendra` → Erstellen.
3. Es erscheinen **Client-ID** und **Clientschlüssel**. Fenster offen lassen.
   Diese beiden Werte nicht weitergeben, nur in Kalendra eintragen.

### 4. In Kalendra
1. Kalendra starten → links unten **Konto verbinden …** (bzw. **＋ Konto hinzufügen …**) → **Google-Konto (bearbeiten)**.
2. Client-ID und Clientschlüssel einfügen → **Im Browser anmelden**.
3. Im Browser das Gmail-Konto wählen. Falls „Google hat diese App nicht überprüft“ erscheint:
   **Erweitert → Weiter zu Kalendra (unsicher)**. Das ist deine eigene App, das ist hier normal.
4. Zugriff auf den Kalender erlauben → „Anmeldung erfolgreich“ → zurück zu Kalendra.

Die Google-Kalender erscheinen links unter **GOOGLE**. Rechtsklick auf ⟳ zeigt Konto,
Sync-Protokoll, „Erneut anmelden“ und „Abmelden“.

Wenn etwas nicht klappt: links unten **Protokoll anzeigen** öffnen und den Inhalt schicken
(es enthält keine Passwörter oder Schlüssel).
