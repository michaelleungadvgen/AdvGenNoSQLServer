1. **Optimize `ActiveLockCount` and `WaitingRequestCount` in `LockManager.cs`**
   - Replace the `_resourceLocks.Values.Sum(...)` and `_waitingQueues.Values.Sum(...)` with simple `foreach` loops that iterate directly over the respective dictionaries. This avoids multiple enumerations and internal array allocations.

2. **Optimize `GetCollectionStats` in `MvccDocumentStore.cs`**
   - In `GetCollectionStats`, iterate directly over `collection` in a `foreach` loop to sum the version counts instead of calling `.Values.Sum(c => c.VersionCount)`. This eliminates the O(N) allocation of the `Values` property.

3. **Complete pre-commit steps to ensure proper testing, verification, review, and reflection are done.**
   - Run relevant tests and update the Bolt journal.

4. **Submit PR**
   - Submit the PR with the required Bolt format.
