# HARDWARE — till peripherals

Written by Stage 09, which is the stage that first needed any of it. `CLAUDE.md` §5 has listed
`src/VumaRetail.Hardware/` since Stage 00 and `PROGRESS.md` §4.1 has listed this document as owed
since the same day.

Scope: the receipt printer, the cash drawer, the barcode scanner, the weighing scale and the payment
terminal. Not the PC, the network or the UPS — those are deployment concerns and belong to Stage 31.

---

## 1. The one decision this project made about hardware

**The abstraction is cross-platform; only the transports are not.**

`VumaRetail.Hardware` targets `net9.0`, not `net9.0-windows`, even though the product ships on
Windows (ADR-031). What lives in it is the *protocol* half of hardware — ESC/POS byte sequences, a
raw TCP socket, the digits inside a price-embedded barcode, the layout of an 80mm slip — and none of
that is Windows-specific. It is also the half that is genuinely hard to get right, so it is worth
being testable on every machine a developer has rather than only on the one that can build WPF.

The consequence is visible in the test suite: the receipt layout, the ESC/POS stream and the scale
label reader are all asserted on the Linux box this repository is developed on, for devices that only
exist on a shop counter.

What is *not* here, and why:

| Deferred | Why | Whose |
|---|---|---|
| Serial (RS-232) and USB printer transports | `System.IO.Ports` and the USB printing path are Windows-installer territory, and neither can be exercised here | Stage 31 |
| OPOS / POS-for-.NET scanner integration | A vendor driver stack that has to be installed before it can be spoken to | Stage 31 |
| A real payment terminal SDK | There is no acquirer contract. `SimulatedPaymentTerminal` is the tested default — the same position `InProcessControlPlane` (04b) and `FileSystemBackupVault` (04) hold | Stage 30b / commercial |
| A real weighing scale protocol | No scale on this machine, and the protocols are per-manufacturer. `SimulatedWeighingScale` reads a fixed weight | whoever has one |

Each of these is an implementation of an interface that already exists and is already called. None
of them changes a caller.

---

## 2. Receipt printer

### How it is actually attached

In a shop, the till printer usually has an Ethernet port, a fixed IP and accepts raw bytes on TCP
9100 — the JetDirect convention. There is no protocol above the socket: whatever you send, the
printer executes. `NetworkReceiptPrinter` is that, and it is the transport most stores will use.

`TextFileReceiptPrinter` writes each job to a file. It is the development printer, the one a demo
runs on, and the fallback for a terminal with no printer configured — deliberately not a null object,
because a receipt you can open and read is worth more than a silent success.

### Why the connect timeout is short

Three seconds, not the operating system's default. A till that blocks for thirty seconds because
somebody unplugged the printer is a till that has stopped serving customers, and R1 does not allow
that. Failing fast lets the caller record the sale and reprint later — which is exactly what
`ReceiptPrint` and the reprint endpoint exist for.

### ESC/POS

`EscPos` holds the control sequences; `ReceiptRenderer` turns a `ReceiptDocument` into either
fixed-width text or the byte stream. Both come out of one layout pass, so a screen preview, an
emailed copy and the paper slip cannot disagree about what the customer was handed.

Three things in there are worth knowing before changing them:

- **The cut feeds three lines first.** The cutter sits above the print head. Without the feed, the
  last three lines of every receipt are cut off — which looks like a layout bug and is not.
- **The encoding is Latin-1, not UTF-8 and not code page 437.** An ESC/POS printer does not speak
  UTF-8: a multi-byte sequence is read as several single-byte characters, some of which are commands.
  437 was rejected because it has no accented lower-case vowels at all.
  `ReceiptRenderer.Transliterate` folds what Latin-1 cannot carry — curly quotes, en and em dashes,
  ellipses, the ones that arrive with every description pasted out of a supplier's spreadsheet.
  It is an explicit table rather than Unicode normalisation because this solution builds with
  `InvariantGlobalization`, under which `string.Normalize` is a no-op; a decomposition-based
  implementation compiles, passes review and silently does nothing.
- **42 characters per line** is font A on 80mm paper. A 58mm printer is 32, and `RenderLines` takes
  the width as an argument — nothing in the layout assumes the default.

---

## 3. Cash drawer

**The drawer is wired to the printer, not to the PC.** It plugs into a modular jack on the back of
the receipt printer, and opening it means sending the printer a pulse on the drawer-kick pin. That is
why `PrinterKickCashDrawer` takes an `IReceiptPrinter`, and why a terminal with no printer has no
drawer to open.

`ICashDrawer` exists separately anyway, because "open the drawer" is what the application wants and
"pulse pin 2" is one way of doing it. A self-checkout with a coin recycler opens nothing and
satisfies the same interface.

The drawer kick is appended to the receipt only when the caller asks for it. A card sale should not
spring the drawer open in front of the customer.

---

## 4. Barcode scanner

A till scanner is a keyboard-wedge device: it types the digits and presses Enter. There is nothing to
integrate at the transport level for the common case, which is why there is no `IBarcodeScanner`
interface here — the scan arrives as text, from the UI or from the API.

What *does* need code is reading the scan, and `BarcodeScanReader` is where the real work is.

### Price- and weight-embedded labels

Every deli, butchery and produce scale in a South African supermarket prints its own EAN-13. The
first two digits are a reserved prefix, the next five are the item's code, and the next five are the
weight or the price **of that specific package**. Scan two identical trays of mince and you get two
different barcodes.

A till that treats these as plain product codes finds neither in the catalogue. So:

| Prefix | Means | Value field |
|---|---|---|
| `21`, `22`, `23` | weight embedded | grams |
| `24`, `25`, `26`, `02` | price embedded | cents |
| anything else | a plain product barcode | — |

GS1 reserves `02` and `20`–`29` for exactly this and leaves the meaning of the five value digits to
the retailer, so the split above is a convention, not a standard. It is a constructor argument: a
tenant whose labeller does it differently configures rather than forks.

**The check digit is verified before any of this is believed.** A misread digit is far more likely
than a genuine `21`-prefixed product code, and ringing up a weight nobody weighed is worse than
failing to find the product. A label that does not check out is returned as a plain product code.

---

## 5. Weighing scale

`IWeighingScale` reads a stable weight and nothing else. It does not price it — a scale that knew
prices would be a second place a price lives, and pricing is Stage 10's (ADR-072).

`ScaleNotStableException` exists because a weight taken mid-swing is not a weight anybody may be
charged for. A real driver waits for the manufacturer's stable indicator; the simulator is always
stable.

---

## 6. Payment terminal

`IPaymentTerminal` is deliberately narrow: an amount and a reference in, an approval or a decline
out. **No card data crosses it.** `PaymentAuthorisation` carries an authorisation code and a masked
PAN and has nowhere to put a full one — a full PAN must never reach Vuma's storage, and the way to
guarantee that is to give it nowhere to go rather than to remember not to store it.

`SimulatedPaymentTerminal` approves everything and derives its authorisation code from the sale
reference, so the same sale always simulates the same authorisation. Deterministic rather than
random, because a receipt test that prints an authorisation code should not be flaky.

---

## 7. What a stage adding hardware should do

1. Implement the existing interface. Do not add a parallel one.
2. Keep the transport out of `VumaRetail.Hardware` if it needs a platform — a `net9.0-windows`
   project referencing this one is the shape, the same way `VumaRetail.Desktop` will.
3. Anything that parses a device's output belongs *here*, tested, whatever platform reads the bytes.
4. If the device can be slow or absent, fail fast and let the caller carry on. R1 outranks every
   peripheral in this document.
