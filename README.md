# NSTProxy

The idea behind this project is simple. We work with Business Central, which is a cloud service. BC uses the local printers for the native reports, everything fine there.

But in order to print using text drivers, by sendind raw data directly to the printer, which is necessary if you are printing to a POS Recipt printer, for example, or a ZPL/EPL printer, you need to send RAW text.

This is the simple task of this app, exposing the printers via a local webservice endpoint, so I can send the data to that endpoint, and the service in turn hands the Raw data to the printer directly.

If the scenario serves you, Nice.
