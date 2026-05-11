# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.11.40] - 2026-05-10

### Fixed
- `$slice` projection now handles MongoDB driver's aggregation expression format (`["$field", count]`) — previously threw `FormatException` on `$scores`
- `$slice` projection is correctly treated as mode-neutral (exclusion by default) — previously forced inclusion mode
- `$slice` and `$elemMatch` projections now support dot-notation for nested arrays
- Duplicate index key detection: creating an index with same key but different name now correctly throws `MongoCommandException` (code 86) — previously allowed silent duplicates
- `MongoCommandException` in index manager now uses `SyntheticConnectionId` instead of `null` — previously threw `ArgumentNullException`

### Added
- `$jsonSchema` filter now supports `allOf`, `anyOf`, `oneOf`, and `not` composition operators — previously silently ignored
- 13 new integration tests covering all fixes and new features

## [0.11.39] - 2026-05-10

### Fixed
- `$setField` with `$$REMOVE` value now correctly removes the field — previously leaked sentinel value into the document
- `$bit` update on missing field now creates `Int64` when operand is `Int64` — previously always created `Int32`

### Added
- `$isoDayOfWeek` aggregation expression operator — returns ISO 8601 day of week (Monday=1, Sunday=7)
- `$unsetField` aggregation expression operator — removes a field from a document (alias for `$setField` with `$$REMOVE`)
- `$median` group accumulator — returns the 50th percentile value
- `$percentile` group accumulator — returns an array of values at specified percentile points
- 8 new integration tests covering all fixes and new operators

## [0.11.38] - 2026-05-10

### Fixed
- `$dateDiff`, `$dateAdd`, `$dateSubtract` now support `"quarter"` unit — previously threw `Unknown date unit: quarter`

### Added
- `$dateTrunc` aggregation expression operator — truncates dates to specified unit boundaries with optional `binSize`, `startOfWeek` support
- `$dateFromParts` aggregation expression operator — constructs dates from calendar parts or ISO week date parts
- `$dateToParts` aggregation expression operator — decomposes dates into constituent parts (calendar or ISO 8601)
- `$week` date extractor — returns Sunday-based week of year (0-53)
- `$isoWeek` date extractor — returns ISO 8601 week number (1-53)
- `$isoWeekYear` date extractor — returns ISO 8601 year number
- Bitwise aggregation expression operators: `$bitAnd`, `$bitOr`, `$bitXor`, `$bitNot`
- 21 new integration tests covering quarter unit bug, $dateTrunc, $dateFromParts, $dateToParts, $week/$isoWeek/$isoWeekYear, and bitwise operators

## [0.11.37] - 2026-05-10

### Fixed
- Positional `$` update operator now works with query operators (`$gte`, `$in`, etc.) on scalar arrays — previously only matched exact equality
- `FindMatchedArrayIndex` now uses `BsonFilterEvaluator.Matches` for query operator conditions instead of direct `.Equals()`

### Added
- `$indexOfArray` aggregation expression operator — searches array for first occurrence of a value with optional start/end bounds
- 7 set expression operators: `$setUnion`, `$setIntersection`, `$setDifference`, `$setEquals`, `$setIsSubset`, `$anyElementTrue`, `$allElementsTrue`
- 14 new integration tests covering positional $ with query operators, $push/$addToSet missing field behavior, $indexOfArray, and all set operators

## [0.11.36] - 2026-05-10

### Fixed
- `DropCollection` SDK path now removes all metadata (views, validators, timeseries options) — previously only removed stores and `_explicitlyCreated`, leaking stale metadata
- `RunCommand` `create` with `viewOn` now properly registers the view — previously created a regular collection, ignoring the view pipeline
- `Aggregate` on a view now applies the view pipeline and TTL eviction — previously bypassed `GetStoreDocuments()` and used raw `_store.GetAll()`
- Schema validation (`ValidateDocument`) is now enforced on all write paths: `InsertOne`, `InsertMany`, `ReplaceOne`, `UpdateOne`, `UpdateMany`, `FindOneAndReplace`, `FindOneAndUpdate`, `BulkWrite` — previously only index uniqueness was validated
- `RenameCollection` now migrates validators, views, and timeseries metadata to the new name — previously lost all metadata on rename

### Added
- 7 new integration tests covering DropCollection metadata cleanup, Aggregate on views, schema validation enforcement on writes, and RenameCollection metadata migration

## [0.11.33] - 2026-05-11

### Fixed
- `InsertMany` with ordered=true now wraps duplicate key errors in `MongoBulkWriteException` — previously threw raw `MongoWriteException`
- `InsertMany` ordered error now includes `InsertedCount`, `WriteErrors[].Index`, and `UnprocessedRequests`

### Added
- 2 new integration tests covering `InsertMany` ordered exception type and `$addFields` nested document expressions

## [0.11.31] - 2026-05-11

### Fixed
- **CRITICAL**: `$group` with document `_id` expression (e.g., `{ fieldA: "$a", fieldB: "$b" }`) now correctly evaluates field references — previously returned the literal expression as the grouping key, collapsing all documents into one group
- `$count` aggregation stage on empty input now returns no documents — previously returned `{ total: 0 }`
- `$push` on a field with `null` value now throws proper error — previously silently created an array
- `$addToSet` on a field with `null` value now throws proper error — previously silently created an array

### Added
- 4 new integration tests covering `$group` document `_id`, `$count` empty input, `$push`/`$addToSet` null field handling

## [0.11.28] - 2026-05-11

### Fixed
- `$inc` on a field with `null` value now throws proper error — previously silently treated null as 0
- `$mul` on a field with `null` value now throws proper error — previously silently treated null as 0
- `$rename` on a field with `null` value now correctly renames it — previously treated null as missing and did nothing
- `$min` on a field with `null` value now correctly keeps null (null < numbers in BSON comparison order) — previously always replaced null with the new value
- `$all` filter operator now matches scalar field values — previously only matched arrays

### Added
- 5 new integration tests covering `$inc`/`$mul`/`$rename`/`$min` null-value handling and `$all` scalar matching

## [0.11.27] - 2026-05-11

### Fixed
- `$range` with step=0 now throws proper `MongoCommandException` — previously caused infinite loop
- `$round` with null input now returns null — previously threw `InvalidCastException`
- `$trunc` with null input now returns null — previously threw `InvalidCastException`
- `$indexOfBytes` with start index beyond string length now returns -1 — previously threw `ArgumentOutOfRangeException`
- `$indexOfBytes` with null substring now throws proper `MongoCommandException` — previously incorrectly returned null
- `$zip` with null input arrays now returns null — previously threw `InvalidCastException`
- `$mergeObjects` with non-document input now throws proper `MongoCommandException` — previously threw `InvalidCastException`

### Added
- 7 new integration tests covering `$range`/`$round`/`$trunc`/`$indexOfBytes`/`$zip`/`$mergeObjects` edge cases

## [0.11.26] - 2026-05-11

### Fixed
- `$sortArray` on non-array input now throws proper `MongoCommandException` — previously threw `InvalidCastException`
- `$toObjectId` on non-string input now throws proper `MongoCommandException` — previously threw `InvalidCastException`
- `$toObjectId` with invalid ObjectId string now throws proper `MongoCommandException` — previously threw unhandled `FormatException`
- `$split` on non-string arguments now throws proper `MongoCommandException` — previously threw `InvalidCastException`
- `$dateFromString` on non-string `dateString` now throws proper `MongoCommandException` — previously threw `InvalidCastException`
- `$trim`/`$ltrim`/`$rtrim` with `null` chars parameter now correctly returns `null` — previously threw `InvalidCastException`

### Added
- 5 new integration tests covering `$sortArray`/`$toObjectId`/`$split`/`$dateFromString` type validation and `$trim` null chars handling

## [0.11.25] - 2026-05-11

### Fixed
- `CreateIndex` with same name but different key specification now throws `MongoCommandException` — previously silently returned without creating the new index
- `$reduce` on non-array input now throws proper `MongoCommandException` — previously threw `InvalidCastException`
- `$reverseArray` on non-array input now throws proper `MongoCommandException` — previously threw `InvalidCastException`
- `$slice` (aggregation) on non-array first argument now throws proper `MongoCommandException` — previously threw `InvalidCastException`

### Added
- 4 new integration tests covering CreateIndex name conflict validation, and `$reduce`/`$reverseArray`/`$slice` non-array input validation

## [0.11.24] - 2026-05-11

### Fixed
- `$set` (and other update operators) through a scalar path now throws proper `MongoCommandException` (PathNotViable) — previously silently overwrote the scalar value with a new document, corrupting data
- `$objectToArray` on non-document input now throws proper `MongoCommandException` — previously threw `InvalidCastException`
- `$filter` on non-array input now throws proper `MongoCommandException` — previously threw `InvalidCastException`
- `$map` on non-array input now throws proper `MongoCommandException` — previously threw `InvalidCastException`

### Added
- 4 new integration tests covering update path-through-scalar validation, `$objectToArray` type validation, and `$filter`/`$map` non-array input validation

## [0.11.23] - 2026-05-11

### Fixed
- `BulkWrite` with `ordered:false` now correctly reports write errors via `MongoBulkWriteException` — previously silently swallowed errors and returned success
- `$all` query operator now supports nested `$elemMatch` expressions (`{$all: [{$elemMatch: {...}}]}`) — previously only did literal equality matching
- `$mod` query operator now uses exact comparison instead of fuzzy tolerance (0.0001) — previously could produce false positives for near-match remainders

### Added
- 4 new integration tests covering BulkWrite unordered error reporting, `$all` with `$elemMatch`, and `$mod` exact comparison

## [0.11.22] - 2026-05-11

### Fixed
- `$getField` with null input now returns `null` — previously threw `InvalidCastException`
- `$setField` with null input now returns `null` — previously threw `InvalidCastException`
- `$concat` with non-string arguments now throws proper `MongoCommandException` — previously threw `InvalidCastException`
- `$trim`/`$ltrim`/`$rtrim` with non-string input now throws proper `MongoCommandException` — previously threw `InvalidCastException`
- `$arrayElemAt` with non-array first argument now throws proper `MongoCommandException` — previously threw `InvalidCastException`
- `$in` (aggregation) with non-array second argument now throws proper `MongoCommandException` — previously threw `InvalidCastException`
- `$concatArrays` with non-array arguments now throws proper `MongoCommandException` — previously threw `InvalidCastException`

### Added
- 7 new integration tests covering `$getField`/`$setField` null input, `$concat`/`$trim` non-string type validation, and `$arrayElemAt`/`$in`/`$concatArrays` non-array type validation

## [0.11.21] - 2026-05-11

### Fixed
- `$toUpper`/`$toLower` on `null` now returns empty string `""` — previously returned `null` (diverging from MongoDB docs)
- `$toUpper`/`$toLower` on non-string values now throws proper `MongoCommandException` — previously threw `InvalidCastException`
- `$size` (aggregation) on non-array values now throws proper `MongoCommandException` — previously threw `InvalidCastException`
- `$strLenBytes`/`$strLenCP` on `null` now throws proper `MongoCommandException` — previously silently returned `null` (MongoDB rejects null arguments)
- `$strLenBytes`/`$strLenCP` on non-string values now throws proper `MongoCommandException` — previously threw `InvalidCastException`
- `$regexMatch`/`$regexFind`/`$regexFindAll` on non-string input now throws proper `MongoCommandException` — previously threw `InvalidCastException`

### Added
- 7 new integration tests covering `$toUpper`/`$toLower` null/non-string handling, `$size` non-array error, `$strLenBytes` null/non-string error, and `$regexMatch` non-string input error

## [0.11.20] - 2026-05-11

### Fixed
- `$split` aggregation expression now returns `null` when the delimiter argument is `null` — previously threw `InvalidOperationException`
- `$strcasecmp` aggregation expression now treats `null` arguments as empty string — previously threw `InvalidOperationException`
- `$replaceOne` aggregation expression now returns `null` when find or replacement argument is `null` — previously threw `InvalidOperationException`
- `$replaceAll` aggregation expression now returns `null` when find or replacement argument is `null` — previously threw `InvalidOperationException`
- `$indexOfBytes` aggregation expression now returns `null` when the substring argument is `null` — previously threw `InvalidOperationException`

### Added
- 7 new integration tests covering aggregation expression null handling for `$concat`, `$toUpper`, `$size`, `$split`, `$indexOfBytes`, `$replaceOne`, and `$strcasecmp`

## [0.11.19] - 2026-05-11

### Fixed
- `Distinct` now includes `null` in results for missing fields, explicit null values, and null array elements — previously silently excluded all null values
- `ListDatabases` and `ListDatabaseNames` now exclude empty databases (matching MongoDB default behavior) — previously included databases with no collections

### Added
- 6 new integration tests covering Distinct null/missing field handling, `$group` `$push` with missing values, and ListDatabaseNames empty database filtering

## [0.11.18] - 2026-05-11

### Fixed
- Projection now validates against mixing inclusion and exclusion modes (except `_id`) — previously silently produced incorrect results
- `$bit` on non-numeric field now throws proper error instead of silently replacing with integer result

### Added
- 7 new integration tests covering projection mode validation, `$bit` non-numeric error, `$inc`/`$mul` on missing fields, `$addToSet`/`$push` on non-array fields

## [0.11.17] - 2026-05-11

### Fixed
- `$pop` on non-array field now throws proper error instead of silently no-oping
- `$pull` on non-array field now throws proper error instead of silently no-oping
- `$pullAll` on non-array field now throws proper error instead of silently no-oping
- `$inc` on non-numeric field now throws `MongoCommandException` instead of `InvalidCastException`
- `$mul` on non-numeric field now throws `MongoCommandException` instead of `InvalidCastException`

### Added
- 7 new integration tests covering array operator type validation, numeric operator type validation, and `$rename` edge cases

## [0.11.16] - 2026-05-11

### Fixed
- `$not` filter operator now correctly handles missing fields — previously wrapped field value in a synthetic document, corrupting the `fieldExists` state and causing incorrect matches
- `CreateUpsertDocumentFromFilter` now correctly extracts document equality values from filters (e.g., `{ nested: { a: 1, b: 2 } }`) and recursively handles `$and` conditions
- `$push` update operator now validates that `$sort`, `$slice`, and `$position` modifiers require the `$each` modifier — previously silently pushed them as literal document values

### Added
- 6 new integration tests covering `$not` with missing fields, upsert filter extraction (`$and`, document equality), `$graphLookup` deduplication, and `$push` modifier validation

## [0.11.15] - 2026-05-11

### Fixed
- `RemoveFieldPath` now handles numeric array indices in dot-notation paths (e.g., `$unset: { "items.0.qty": "" }`) — previously silently no-oped
- `$unset` on array elements by numeric index now correctly sets elements to `null` (matching MongoDB behavior) instead of ignoring them
- Added missing `FaultInjector` and `OperationLog` support to `FindOneAndReplace`, `FindOneAndUpdate`, `DeleteMany`, `UpdateMany`, and `ReplaceOne`

### Added
- 7 new integration tests covering RemoveFieldPath array indexing and OperationLog coverage for all write operations

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
