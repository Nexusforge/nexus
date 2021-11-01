Nexus relies on [Serilog](https://serilog.net/) to produce logs. Serilog completely replaces the standard logging mechanism, which means that the `Logging` configuration section is being ignored and the `Serilog` section is used instead. Therefore, the default `Logging` section has been removed from the [appsettings.json file](https://github.com/Nexusforge/Nexus/blob/master/src/Nexus/appsettings.json).

Currently, the following sinks and enrichers are supported by Nexus:

**Sinks**
- [Console](https://github.com/serilog/serilog-sinks-console) *enabled by default*
- [Debug](https://github.com/serilog/serilog-sinks-debug)
- [File](https://github.com/serilog/serilog-sinks-file)
- [Grafana.Loki](https://github.com/serilog-contrib/serilog-sinks-grafana-loki)
- [Seq](https://github.com/serilog/serilog-sinks-seq)

**Enrichers**
- [ClientInfo](https://github.com/mo-esmp/serilog-enrichers-clientinfo)
- [CorrelationId](https://github.com/ekmsystems/serilog-enrichers-correlation-id)
- [Environment](https://github.com/serilog/serilog-enrichers-environment)

The default log level is `Information`, which can be easily modified using one of the methods shown in [[Configuration]]. For example, you could set the environment variable `NEXUS_SERILOG__MINIMUMLEVEL__OVERRIDE__Nexus` to `Verbose` to also receive `Trace` and `Debug` message.

[Here](https://github.com/Nexusforge/Nexus/blob/master/tests/Nexus.Core.Tests/Other/LoggingTests.cs) you can find some more examples to enable the `File`, `GrafanaLoki` or `Seq` using environment variables. You can do the same using your own configuration file or command line args as shown in [[Configuration]].
