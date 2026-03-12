This is a passion project for a sim racing title called Lemans Ultimate. Massively still a work in progress.

This app makes use of duckdb files in order to interpret telemetry from the in-game data channels (these data channels are saved in the duckdb file).
Upon launch, a user must upload their duckdb file. This will allow them to see their data such as where on track they are applying inputs, this will allow for analysis of performance. 
Further plans are to incorporate track maps for data to be laid over on, as well as clean up the UI. 

Track maps were attempted to be generated in a Motec style; I would record several laps, convert the GPS points to meters, average the paths to get a smooth centerline spline, and then express every telemetry point as distance along that centerline plus sideways offset instead of trying to match it to a picture of the track. This proved to be too unreliable.
Another method is planned for implementation.

Far future plans include:
Agentic coaching + car setup assistance/guidance.
Database implementation for storing long term data instead of relying on duckdb interpretation.


UI so far:
<img width="1920" height="1032" alt="image" src="https://github.com/user-attachments/assets/993ce559-ae36-41c3-8560-d342479cddfd" />
