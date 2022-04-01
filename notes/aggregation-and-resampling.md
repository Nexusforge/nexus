# Aggregation

When data is being loaded from different data sources, it is often necessary to bring all datasets to the same sample period. Nexus offers a way to specify the target sample period and the processing method for the data request.

Consider the following resource path:

- `/a/b/c/T1/10_ms`

When users want this dataset to be aggregated to 10-minutes mean values, they can simply request the following resource path:
`/a/b/c/T1/10_min_mean`

However, there are cases when a resource owns more than one representation:

- `/a/b/c/T1/10_ms`
- `/a/b/c/T1/100_ms`

With this scenario, users may wish to append a url fragment specifying the base representation to use for data aggregation as shown here:

- `/a/b/c/T1/10_min_mean#base=100_ms`

If no url fragment is appended, Nexus uses the first representation found in the parent resource's list of representations.

*Note:* There other supported aggregation methods like `mean_polar`, `min`, `max`, `std`, etc. The full list is defined in the `RepresentationKind` type.

# Resampling
Resampling works the same way. Simply add the term `resampled` into the resource path (e.g. `/a/b/c/T1/10_min_resampled`).

# Combinations
Not all combination of base sample period and target sample period are possible. See the following table (which uses example periods of `1 s` and `10 min`) to get an overview about supported combinations:

| base → target | 1 s → 1 s | 1 s → 10 min | 10 min → 1 s |
|---------------|-----------|--------------|--------------|
| Aggregation   | ✓         | ✓            | x           |
| Resampling    | ✓         | ✓            | ✓           |
