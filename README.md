# Queue Ticket Management System

A lightweight, self-contained queue ticket system for small offices, clinics, service counters, and customer-facing front desks — runs as a single Windows console application.

![Screenshot Queue Display](https://raw.githubusercontent.com/adriancs2/Queue-Management-System/refs/heads/main/wiki/screenshot-display.png)

![Screenshot My Ticket](https://raw.githubusercontent.com/adriancs2/Queue-Management-System/refs/heads/main/wiki/screenshot-your-ticket.png)

## Features

- **Take-a-number kiosk** — customers tap a department on a touchscreen and a numbered ticket prints automatically.
- **Live display board** — a TV or monitor shows the currently called number and recent history, updated instantly without page refresh.
- **Operator console** — staff at each counter call the next number (or a specific number) with one click.
- **Customer mobile view** — customers can track their position in line from their own phone, with a "Now Serving" view and live updates as their turn approaches.
- **Configurable departments** — define your service categories in a simple text file. No coding required.
- **Crash-safe** — ticket state is persisted to a local database, so an unexpected power loss or restart doesn't lose the queue mid-day.
- **No internet required** — the system runs entirely on your local network. The cloud is optional, not mandatory.
- **Single executable** — no installer, no database server, no web server to manage. Copy the folder, run the .exe.

## How to Setup and How to Use

### 1. First run

Copy the program folder onto the office PC and double-click `SSE.exe`. On first launch, a `config.txt` file is created automatically next to the executable. Close the program, open `config.txt` in Notepad, and edit it.

### 2. Configure the departments

Open `config.txt` and find the `departments=` line. List your service categories separated by commas:

```
http_port=8080
printer_name=
departments=General Inquiries, Payments, License Services, Account & Register
```

- Department IDs are auto-assigned in order (1, 2, 3, ...).
- **Important:** once the office is open and tickets are active, do not reorder or remove departments. Add new ones to the end. Edits are safe before opening, after closing, or during the morning startup.
- `printer_name` can be left blank to use the Windows default printer, or set to the exact Windows printer name (including network printers shared from another PC).
- `http_port` is the TCP port the system listens on. `8080` is fine for local use. For public access from outside the office, change to `80`.

Save the file and run `SSE.exe` again. The console window will show the URLs you can visit.

### 3. The URL endpoints

Open a browser on the office PC (or any device on the same Wi-Fi) and visit:

| URL | Purpose | Where to put it |
|---|---|---|
| `/` | Customer mobile entry page (department selector) | Customer's phone |
| `/terminal` | Touchscreen kiosk that prints paper tickets | Self-service kiosk PC at the entrance |
| `/display` | Big-screen "Now Serving" board | TV or monitor in the waiting area |
| `/operator` | Counter staff console | Each service counter's computer |
| `/m?t=123&d=2` | Personal ticket tracking page | Customer's phone (auto-generated link) |

Each device just needs a browser pointed at the right URL — no app to install, no login.

### 4. Customer mobile access

Customers can track their queue position from their own phone instead of waiting at the counter. They reach the system in one of two ways:

- **At the kiosk** — after printing a paper ticket, the screen shows a link the customer can type or scan.
- **QR code at the counter** — print a QR code that points to your system's address (e.g. `https://queue.your-office.com/`) and place it at the counter or entrance. Customers scan it, choose their department, and get a personal tracking page.

You generate the QR code yourself once (any free QR generator online), print it, laminate it, place it where customers can see it. Done.

## Hardware Setup

These are one-time setup steps the office's IT person needs to do:

- **Printer** — connect a thermal receipt printer (e.g. Epson TM-T82) to the kiosk PC, or share a printer from another PC on the network. Put its name in `config.txt`.
- **Firewall** — allow inbound TCP traffic on the port you chose (`8080` by default, `80` for public access) on the host PC's Windows Firewall.
- **Router** — if you want customers to reach the system from outside the office (e.g. checking position from the parking lot), forward the chosen port from your router to the host PC.
- **HTTPS / public domain** *(optional)* — for serving a public domain over HTTPS, use the companion **QMS SSL Setup** utility (see below). For local-only office use, plain HTTP on the LAN is fine.

## HTTPS Setup (Optional)

If you want customers to reach your queue system over the public internet on a real domain (e.g. `https://queue.your-office.com`), the included **QMS SSL Setup** Windows utility automates the certificate process for you.

**What it does in one click:**

- Requests a free SSL certificate from Let's Encrypt for your domain (using the bundled `win-acme` tool).
- Binds the certificate to port 443 on your PC so HTTPS works.
- Adds the required Windows Firewall rules (ports 80 and 443).
- Schedules automatic certificate renewal so it never expires.

**What you'll need before running it:**

- A domain name pointing to your office's public IP address (the tool shows you the public IP — copy it into your DNS provider's A record).
- Router port forwarding for both port 80 (for the certificate validation handshake) and port 443 (for HTTPS traffic).
- Run the utility as Administrator (it modifies firewall and certificate stores).

Fill in your domain and email, click **Run SSL Setup**, and the tool walks through every phase with status output. Use **Renew Now** anytime to force-refresh, or **Remove SSL Binding** to clean up.

Once setup completes, restart the queue system on port 443 and your customers can scan a QR code at the counter that opens the service over a secure public URL.

## License

[Unlicense](https://unlicense.org/) — Public Domain Freeware. Use it, modify it, sell it, embed it. No attribution required, no warranty given.
