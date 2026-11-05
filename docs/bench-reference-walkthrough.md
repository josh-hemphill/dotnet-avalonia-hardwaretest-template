# Authoring to execution with golden references

Use `review/authoring-to-execution` to review both applications together.
The reviewed workflow uses mock instruments; use a separate bench data folder
when experimenting. Required instrument packages must be installed on the bench.

1. In authoring, create a plan with a title and add the measurement steps. Select
   the relevant measurement in **Sequence**. Give it its real unit, then enable
   **Require golden reference** in the inspector. Set a reference input key,
   absolute or percent comparison, and tolerance. **Add criterion** creates an
   independent criterion; **Criterion to edit** selects which one you are editing.
   A scalar reference is the arithmetic mean of the declared measurement's samples.
   Percent comparison requires a nonzero reference value.
2. Save and compile. In **Build**, select **Prepare bench files** and choose the
   idle operator bench's Programs folder or a local transfer folder. Copy the
   entire prepared program folder when transferring to another machine. Keep
   the plan, operator settings and receipt together, then reload bench programs.
3. On the operator bench, select the program and open **Golden references**.
   For the first reference, enable **Reference acquisition**, identify the golden
   DUT and technician, confirm preparation, and explicitly run the acquisition.
   A selected acquisition can supply only the measurements it actually collected.
   Acquisition does not qualify a production unit or station health.
4. Open the completed run in Results and choose **Save golden reference from this
   run**. Select the covered inputs, enter a descriptive profile name and optional
   validity period, then save. Separate profiles can supply different criteria.
   The saved profile records the actual values and source run/DUT/technician.
5. Return to **Golden references**, turn acquisition off, choose a compatible
   profile for every required test, and **Save choices**. Change session for the
   production DUT, confirm the displayed inputs, and run. A reference mismatch
   can fail the final result even when the raw instrument/backend result passes.
6. Inspect the reference acceptance rows and Results for the exact measured mean,
   reference value, tolerance, deviation and source identity. Reports and exports
   retain the frozen inputs used by that run. Import that exact exported recording
   into authoring to inspect it or preview proposed criterion changes.

Changing the program or effective instrument configuration can make an existing
profile incompatible. Reacquire and save a new profile for the revised program.
**Saved profiles and retirement** prevents future use of obsolete profiles;
earlier results retain their original values and provenance. A replacement gets
a new identity rather than overwriting an earlier profile.

Incomplete authoring documents can be saved for recovery, but compilation and
bench preparation require valid criteria. Expired, retired, corrupt or incompatible
profiles block new acceptance. Mock and physical instrument contexts are distinct.
Previously accepted runs keep their frozen inputs even if a profile expires or
is retired later.

The persona contract and review evidence are in
[cross-app-persona-workflows.md](cross-app-persona-workflows.md) and
[cross-app-persona-review.md](cross-app-persona-review.md).
