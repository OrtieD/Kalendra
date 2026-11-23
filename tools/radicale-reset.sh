#!/bin/bash
pkill -f "radicale --config" ; sleep 1
rm -rf /tmp/w/rad/data; mkdir -p /tmp/w/rad; printf "anna:x\ngmail:y\n" > /tmp/w/rad/users
cat > /tmp/w/rad/cfg <<'C'
[server]
hosts = 127.0.0.1:5232
[auth]
type = htpasswd
htpasswd_filename = /tmp/w/rad/users
htpasswd_encryption = plain
[storage]
filesystem_folder = /tmp/w/rad/data
C
(python3 -m radicale --config /tmp/w/rad/cfg > /tmp/w/rad/rad.log 2>&1 &); sleep 2
U="curl -s --noproxy 127.0.0.1 -u anna:x"
$U -X MKCOL http://127.0.0.1:5232/anna/ -o /dev/null
for c in "privat|Privat|#63DA38FF" "arbeit|Arbeit|#1BADF8FF"; do IFS='|' read id name col <<< "$c"
$U -X MKCOL http://127.0.0.1:5232/anna/$id/ -H 'Content-Type: application/xml' --data "<?xml version=\"1.0\"?><mkcol xmlns=\"DAV:\" xmlns:C=\"urn:ietf:params:xml:ns:caldav\" xmlns:A=\"http://apple.com/ns/ical/\"><set><prop><resourcetype><collection/><C:calendar/></resourcetype><displayname>$name</displayname><A:calendar-color>$col</A:calendar-color><C:supported-calendar-component-set><C:comp name=\"VEVENT\"/></C:supported-calendar-component-set></prop></set></mkcol>" -o /dev/null -w "$id %{http_code}\n"
done
G="curl -s --noproxy 127.0.0.1 -u gmail:y"
$G -X MKCOL http://127.0.0.1:5232/gmail/ -o /dev/null
$G -X MKCOL http://127.0.0.1:5232/gmail/termine/ -H 'Content-Type: application/xml' --data '<?xml version="1.0"?><mkcol xmlns="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav" xmlns:A="http://apple.com/ns/ical/"><set><prop><resourcetype><collection/><C:calendar/></resourcetype><displayname>test@gmail.example</displayname><A:calendar-color>#9FE1E7FF</A:calendar-color></prop></set></mkcol>' -o /dev/null -w "gmail %{http_code}\n"
pkill -f fake_google.py; (python3 $(dirname "$0")/FakeGoogle/fake_google.py > /tmp/w/rad/fg.log 2>&1 &); sleep 1
