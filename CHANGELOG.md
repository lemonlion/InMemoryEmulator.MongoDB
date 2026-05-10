# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.11.14] - 2026-05-11

### Fixed
- `$bit` operator now supports positional operators (`$`, `$[]`, `$[identifier]`)
- `SetFieldPath` now handles numeric array indices in dot-notation paths (e.g., `items.1.qty`) instead of overwriting them with subdocuments
- `FindOneAndDelete` now records to `OperationLog` and invokes `FaultInjector` like other write operations
- `$bucketAuto` aggregation stage now supports custom `output` accumulators instead of only returning `count`

### Added
- 7 new integration tests covering $bit with positional, numeric array indices, FindOneAndDelete operation logging, and $bucketAuto output

## [0.11.13] - 2026-05-11

### Fixed
- Array update operators (`$push`, `$pull`, `$pullAll`, `$addToSet`, `$pop`) now support positional operators (`$`, `$[]`, `$[identifier]`) for nested array paths
- `BulkWrite` now correctly forwards `ArrayFilters` from `UpdateOneModel` and `UpdateManyModel` to the underlying update operations

### Added
- 7 new integration tests covering array operators with positional paths and BulkWrite arrayFilters passthrough

## [0.11.12] - 2026-05-11

### Added
- Positional update operator `$` — updates the first array element matching the query filter
- All positional update operator `$[]` — updates all elements in an array
- Filtered positional update operator `$[<identifier>]` — updates array elements matching `arrayFilters` conditions
- Support for positional operators in `$set`, `$unset`, `$inc`, `$mul`, `$min`, `$max`, `$currentDate`, and `$setOnInsert`
- Positional operators work with `UpdateOne`, `UpdateMany`, and `FindOneAndUpdate`
- 9 new integration tests covering all three positional operator variants

## [0.11.11] - 2026-05-11

### Fixed
- `$cond` array form now validates exactly 3 arguments and throws a descriptive error instead of `ArgumentOutOfRangeException`

### Added
- 3 new integration tests covering $cond validation and both array/document forms

## [0.11.10] - 2026-05-11

### Fixed
- Aggregation arithmetic operators (`$add`, `$subtract`, `$multiply`, `$mod`) now preserve integer types (Int32/Int64) instead of always returning Double
- `$subtract` on two dates now correctly returns Int64 (milliseconds) instead of Double
- `$elemMatch` projection now works with scalar array elements (numbers, strings), not just embedded documents

### Added
- 12 new integration tests covering arithmetic type preservation, $elemMatch scalar projection

## [0.11.9] - 2026-05-11

### Fixed
- `$unwind` now treats scalar values (string, number, subdocument) as single-element arrays instead of dropping the document
- `$unwind` `preserveNullAndEmptyArrays` now correctly handles dot-notation paths for nested field removal
- `$group` `$sum` now preserves integer types (returns Int32/Int64 when all inputs are integers, Double only when inputs contain doubles)
- Aggregation `$project` inclusion with dot-notation paths now creates proper nested document structure instead of flat fields
- Aggregation `$project` exclusion with dot-notation paths now correctly removes nested fields

### Added
- 10 new integration tests covering $unwind scalar values, $unwind nested paths, $sum type preservation, $project dot-notation inclusion/exclusion

## [0.11.7] - 2026-05-10

### Fixed
- `$regex` + `$options` in operator form no longer throws `NotSupportedException` (options are now consumed alongside regex)
- `$round` now uses IEEE 754 round-to-even (was incorrectly using round-half-away-from-zero)
- Upsert inserts now publish change stream `Insert` events (UpdateOne, UpdateMany, ReplaceOne, FindOneAndReplace, FindOneAndUpdate)
- `BulkWrite` `InsertOneModel` now validates unique indexes and publishes change stream events
- `ListCollectionNames` now includes views (not just collections)
- `RunCommand` `distinct` now unwraps array field values into individual elements

### Added
- 10 new integration tests covering regex options, round-to-even, upsert change events, BulkWrite validation, views in ListCollectionNames, RunCommand distinct arrays

## [0.11.6] - 2026-05-10

### Fixed
- `CountDocuments` now honors `CountOptions.Skip` and `CountOptions.Limit`
- `$push` with negative `$position` now correctly inserts relative to the end of the array
- `$unset` aggregation stage now supports dot-notation paths for nested field removal
- Exclusion projection now supports dot-notation paths for nested field exclusion
- `$strcasecmp` now normalizes return value to exactly -1, 0, or 1
- `$add` now supports date arithmetic (Date + milliseconds returns Date)
- `$subtract` now supports date arithmetic (Date - Date returns milliseconds, Date - number returns Date)
- `$pull` with non-operator document conditions now matches subdocument fields instead of requiring exact equality
- `FindOneAndUpdate` now uses atomic `DocumentStore.Update` with correct `Update` change type (was using `Replace`)

### Added
- 14 new integration tests covering CountDocuments options, $push negative position, $unset/$projection dot-notation, $strcasecmp, date arithmetic, $pull subdocument matching

## [0.11.5] - 2026-05-10

### Fixed
- `Distinct` now unwraps array fields into individual elements per MongoDB spec (each array element treated as a separate value)
- `UpdateMany` now uses atomic `DocumentStore.Update` instead of `Replace`, recording correct `Update` change type in change log
- `UpdateMany` now publishes change stream events for each modified document
- `DeleteMany` now publishes change stream events for each deleted document
- `FindOneAndDelete` now publishes change stream delete event
- `FindOneAndReplace` now publishes change stream replace event
- `FindOneAndUpdate` now publishes change stream update event

### Added
- 9 new integration tests covering Distinct array unwinding and change stream event publishing

## [0.11.4] - 2026-05-10

### Fixed
- Array element iteration for comparison, `$regex`, and `$mod` filter operators
- Dot-notation field resolution through arrays
- Implicit `BsonRegularExpression` matching in equality filters
- `$in`/`$nin` with `BsonRegularExpression` values
- `$type` missing aliases (`timestamp`, `javascript`, `minKey`, `maxKey`, etc.) and array element type checking
- Sort now extracts min (ascending) / max (descending) from array fields
- `RenameCollection` atomicity (check target existence before removing source)
- `$substr` no longer crashes with negative start index (returns empty string per spec)

### Added
- 25 new integration tests covering array field matching, edge cases, and bug fixes

## [0.11.0] - 2025-07-18

### Added
- README.md with quick start, feature matrix, and wiki links
- CHANGELOG.md (this file)
- NuGet publish workflow (GitHub Actions, tag-triggered, manual approval gate)
- CodeQL security analysis in CI
- Dependabot configuration for NuGet dependency updates
- `PackageReadmeFile` metadata for NuGet packages
- Additional SdkVersionDriftDetector integration tests
- Vulnerable package check (`dotnet list package --vulnerable`) in CI

## [0.10.0] - 2025-07-18

### Added
- Unique index enforcement (single-field, compound, sparse, partial filter)
- TTL index lazy eviction on read paths
- Index validation on all write paths (Insert, Replace, Update, FindOneAnd*)
- 24 new tests (16 index enforcement + 8 TTL)

## [0.9.0] - 2025-07-18

### Added
- Fault injection via `FaultInjector` delegate (simulate errors, latency)
- Operation logging with `RequestLog` and `QueryLog`
- Concurrency stress tests
- Benchmarks (throughput, latency percentiles)

## [0.8.0] - 2025-07-18

### Added
- Capped collections with `max` and `size` document limits
- Tailable cursors via `Channel<T>` cross-thread notification
- JavaScript expression support (`$function`, `$accumulator`, `$where`) via Jint
- `MongoDB.InMemoryEmulator.JsTriggers` optional package

## [0.7.0] - 2025-07-17

### Added
- GridFS file operations (`IGridFSBucket` implementation)
- Upload, download, find, rename, delete for GridFS files
- Stream-based upload/download support

## [0.6.0] - 2025-07-17

### Added
- `$text` filter operator (case-insensitive word matching)
- Text index creation and text score projection
- Atlas `$search` / `$vectorSearch` stubs (basic substring/brute-force)

## [0.5.0] - 2025-07-17

### Added
- Advanced aggregation stages (`$graphLookup`, `$bucket`, `$bucketAuto`, `$densify`, `$fill`)
- Geospatial query operators (`$geoWithin`, `$geoIntersects`, `$near`, `$nearSphere`)
- Geospatial aggregation (`$geoNear` stage)
- All remaining expression operators
- NetTopologySuite integration for geometric calculations

## [0.4.0] - 2025-07-17

### Added
- Window functions (`$setWindowFields` with `$sum`, `$avg`, `$min`, `$max`, `$rank`, `$denseRank`)
- Change streams (watch collection, database, client)
- Client sessions and multi-document transactions (snapshot isolation)
- Views (`createView` / `db.CreateCollection` with `ViewOn` + pipeline)
- Dependency injection (`UseInMemoryMongoDB`, `UseInMemoryMongoCollections`)
- Schema validation (`$jsonSchema` via `CreateCollection` validator)
- Time series collection stubs

## [0.3.0] - 2025-07-17

### Added
- Full aggregation pipeline (34 stages)
- 100+ expression operators (arithmetic, string, date, array, conditional, type, set)
- LINQ support (`AsQueryable()` with LINQ2 and LINQ3 providers)
- Pipeline-style updates (`UpdateDefinition` from aggregation pipeline)

## [0.2.0] - 2025-07-16

### Added
- All filter operators (comparison, logical, element, array, evaluation, bitwise)
- All update operators (`$set`, `$unset`, `$inc`, `$push`, `$pull`, `$addToSet`, `$rename`, etc.)
- Projection operators (`$slice`, `$elemMatch`, `$meta`)
- Sort, skip, limit on find operations
- Collation support (culture-aware string comparison)
- MongoDB error codes and exception types

## [0.1.0] - 2025-07-16

### Added
- Initial project scaffold (solution, 5 projects, CI pipeline)
- `InMemoryMongoClient`, `InMemoryMongoDatabase`, `InMemoryMongoCollection<T>`
- Basic CRUD operations (InsertOne, InsertMany, Find, ReplaceOne, DeleteOne, DeleteMany)
- `CountDocuments`, `EstimatedDocumentCount`
- `BsonDocument` internal storage with `ConcurrentDictionary`
- xUnit v3 test infrastructure with `TestFixtureFactory` dual-target support
- GitHub Actions CI (in-memory + real MongoDB + weekly Atlas parity)
