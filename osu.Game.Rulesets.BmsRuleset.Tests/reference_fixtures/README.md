# Fixed beatoraja reference fixture

`beatoraja_windows.csv` is the 225-row output of the actual Java `JudgeProperty`
from beatoraja commit `9cddf911113e74da87458344bb85a4ea45431d0b`, exported with
`AuditJudgeReference.java`. The exporter is included here for provenance; it is
not executed by tests and no sibling checkout is required at test runtime.
Columns: property, note type, BMS RANK (0-4), judge index, slow microseconds,
fast microseconds. C# offsets reverse Java's `noteTime - pressTime` sign.

The source is available at:
https://github.com/exch-bms2/beatoraja/blob/9cddf911113e74da87458344bb85a4ea45431d0b/src/bms/player/beatoraja/play/JudgeProperty.java
