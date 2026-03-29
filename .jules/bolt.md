## 2024-03-14 - Use yield return for memory efficiency
**Learning:** Returning materialized lists (e.g., .ToList(), .Values.ToList()) from repository methods for large datasets causes high GC pressure and memory spikes when the results are only subsequently filtered or iterated over.
**Action:** Always use `yield return` or return `IEnumerable<T>` for data retrieval operations like `GetAllAsync` to enable lazy evaluation and reduce memory allocations, especially before passing collections to a filter engine.

## 2024-03-14 - Optimize ExistsAsync with LINQ Any()
**Learning:** In AdvGenNoSqlServer's query execution pipeline, `ExistsAsync` was previously calling `CountAsync` which evaluated the full collection matching a query, resulting in an O(N) operation.
**Action:** Replace `CountAsync(query) > 0` with `.Any()` on the lazily-evaluated document stream returned by `GetManyAsync` or `GetAllAsync`. This short-circuits evaluation as soon as a single matching document is found, improving performance from O(N) to O(1) in the best case.
