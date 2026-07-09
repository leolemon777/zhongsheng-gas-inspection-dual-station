# Implementation Notes

## 2026-05-23 Valve actuation during field debugging

- The inlet valve is position-holding, but it needs continuous power while traveling. The software now treats the configured valve time as an action energize duration rather than a momentary pulse.
- Default valve action time is 5000 ms, with validation allowing 50-30000 ms.
- Valve energizing remains included inside the configured fill/stabilize stage times; no runtime protection blocks shorter stage settings during dry-run testing.
- Valve energizing is interruptible. Starting the opposite action cancels the current open/close action, turns the previous coil off, and immediately starts the new direction.
- Manual IO buttons remain operable during valve travel so operators can switch from open to close, or close to open, before the maximum energize time expires.
- Stop no longer waits for the close-valve action to finish before releasing the run state. It cancels the active run, starts close-valve cleanup in the background, and allows a new start to interrupt that cleanup.
- Manual IO and automatic inspection both route through the same valve action path.
- The Modbus client now supports writing a coil ON, holding the same TCP connection for the configured duration, then writing OFF on that connection. This avoids modules dropping outputs when a per-command TCP connection closes.
- The automatic run page no longer shows valve open/close as separate timed stages. Open-valve energizing is included in the configured fill time, and close-valve energizing is included in the configured stabilize time.
- Automatic runs now start directly with the fill timer and open-valve action. The old startup close-valve wait was removed so the first visible step is immediately "充气".
- P2 sampling is now published as an explicit visible "采集 P2" step before leak-rate calculation and completion, matching the existing P1 sampling step.
- Every run step card displays its own elapsed/total time. Fill, stabilize, and hold use configured durations; P1/P2 sampling steps use a short visible sampling duration so they no longer appear untimed.
- For dry-run field testing, pressure/current/rise/over-pressure runtime protections were removed from the automatic sequence. The flow proceeds through P1/P2 even with zero or abnormal pressure readings; if P1 is 0, leak rate is recorded as 0 to avoid divide-by-zero.
- Standard Modbus TCP response parsing was corrected to treat MBAP length as unit id plus PDU, while the 7-byte header already contains the unit id.
- Verification: `dotnet build` completed with 0 warnings and 0 errors.

## 2026-05-23 IO manual/auto mode visibility

- The IO monitor page now shows the current mode as a prominent status badge near the page title.
- Auto mode uses a solid green selected state; manual mode uses a solid orange selected state and matching badge so the active mode is visually distinct during field testing.
- The mode badge includes the relevant DO channels so operators can quickly confirm whether manual DO operation is available.

## 2026-05-23 Run step color states

- Process cards now use three distinct color states: neutral for waiting, amber for the step currently running, and solid dark green only for completed steps.
- This keeps the old dark green visual meaning aligned with "finished" rather than "currently active".

## 2026-05-23 Win10 x64 deployment package

- Main window now starts maximized so the operator UI fills the 1920x1080 industrial PC display.
- Deployment should use a self-contained `win-x64` publish so the target PC does not need the .NET runtime installed.
- The published app expects `Data/appsettings.json` next to the executable; the inspection record database is created automatically on first run.

## 2026-05-24 Pressure transmitter precision display

- Reviewed the OHR-M2 pressure transmitter manual. The current 0-1.0 MPa, 4-20 mA two-wire configuration matches the software range settings: 4 mA = 0 MPa and 20 mA = 1 MPa.
- Pressure samples are now kept to 6 decimal places in MPa internally.
- Leak-rate math continues to use MPa internally.
- Important limitation: display granularity is not the same as measurement accuracy; the transmitter accuracy class and analog input resolution still determine real measurement uncertainty.

## 2026-05-25 kPa pressure display and leak limit visibility

- Production pressure display, P1/P2 cards, sample messages, hardware readout, and record list now display pressure in kPa with three decimal places.
- The run page now repeats the configured allowed leak rate inside the "检测数据与流程" card next to actual leak rate, so operators can compare actual and limit without looking back to the top parameter strip.

## 2026-05-25 NG confirmation overlay

- When a completed run is judged NG, the run page now shows a blocking NG overlay with product code, actual leak rate, and allowed leak rate.
- The overlay must be acknowledged with the Confirm button before it closes, reducing the chance of an operator missing an NG unit.

## 2026-05-25 Valve DO mapping correction

- Field test showed the physical valve wiring was reversed relative to the previous software mapping: start energized the close direction and stop energized the open direction.
- Runtime configuration now maps inlet open to DO2 and inlet close to DO1.
- The IO monitor output descriptions and mapping hint now derive from the configured open/close coils instead of hard-coded DO1/DO2 text.

## 2026-05-25 Parameter decimal input

- Settings page numeric text boxes now update their bound values when focus leaves the input instead of on every keystroke.
- This allows operators to type intermediate decimal states such as `0.` and final values such as `0.0022` without WPF rejecting the text while it is still being entered.

## 2026-05-25 Leak-rate display precision

- Leak-rate display no longer uses percent formatting.
- Main page allowed leak rate, actual leak rate, NG confirmation text, and record rows now show the raw `(P1-P2)/P1` decimal value, such as `0.0022`, without a percent sign.
- The allowed leak-rate comparison now treats the configured value as the same raw decimal value instead of dividing it by 100.

## 2026-05-25 Idle pressure refresh on run page

- The production run page now refreshes live pressure while hardware is connected and no inspection flow is running.
- Starting a new run no longer clears the live pressure/current display to zero; P1/P2/leak/result are still reset as process values.
- Pressure reads are serialized in the hardware service so the run page, hardware page, and inspection runner do not issue overlapping analog reads to the module.

## 2026-05-25 Pressure smoothing and recorded sample averaging

- The production live pressure display now uses a 5-sample moving average, with idle refresh every 200 ms, to reduce last-digit jitter while still tracking the pressure gauge.
- P1 and P2 are no longer recorded from a single instantaneous read. Each is recorded from 5 pressure reads spaced 100 ms apart, averaged, then used for leak-rate calculation and records.
- The sampling message explicitly marks P1/P2 as 5-read averages so the displayed value remains traceable.

## 2026-06-09 Dual HMI shared module station support

- The app now keeps one active station in memory while the configuration file can store two station profiles that share the same IO and analog module connection settings.
- Existing single-station `appsettings.json` files remain readable. Legacy hardware point fields are treated as station 1, and station 2 defaults to AI2, DO3 close, and DO4 open.
- Station-specific settings include recipe timing/leak parameters, pressure register, valve open/close coils, valve energize time, and analog decimal parsing.
- The hardware page can switch the current HMI station by station id. Switching loads that station profile into the existing recipe and hardware setting objects so the rest of the UI continues to bind to the current station.
- The run page, settings page, hardware page, and IO page now show the active station and AI/DO mapping.
- Automatic and manual valve actions only write the active station's configured open/close coils. The IO page locks all non-active-station outputs to reduce the risk of affecting the other HMI.
- Inspection records now store `station_id` and `station_name`. Existing SQLite databases are migrated with default station id `1` and station name `旧记录`.
- Verification: `dotnet build "E:\Desktop\中央净软气检测试\中盛气检上位机\ZhongshengGasInspectionHmi.sln"` completed with 0 warnings and 0 errors.
