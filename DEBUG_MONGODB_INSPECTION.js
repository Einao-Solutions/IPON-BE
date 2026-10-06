// ============================================================
// MONGODB INSPECTION SCRIPT
// Run this in MongoDB Compass or Mongo Shell to see actual data
// ============================================================

// 1. Check if ANY Availability Searches exist
db.appUsers.aggregate([
  {
    $project: {
      userId: "$Id",
      searchCount: {
        $size: {
          $filter: {
            input: "$OtherApplications",
            as: "app",
            cond: { $eq: ["$$app.ApplicationType", 29] }
          }
        }
      },
      totalApps: { $size: "$OtherApplications" },
      otherApplications: "$OtherApplications"
    }
  },
  { $match: { searchCount: { $gt: 0 } } }
]);

// Expected output:
// {
//   "_id": <some-id>,
//   "userId": "company-a-id",
//   "searchCount": 2,
//   "totalApps": 5,
//   "otherApplications": [
//     { "ApplicationType": 29, "Title": "Search term 1", ... },
//     { "ApplicationType": 29, "Title": "Search term 2", ... },
//     ...
//   ]
// }

// ============================================================
// 2. If searches exist, verify the RIGHT user can see them
// ============================================================

// Get ONE specific user's OtherApplications
db.appUsers.findOne(
  { "Id": "company-a-id" },  // Replace with actual user ID
  { "OtherApplications": 1 }
);

// Expected output:
// {
//   "_id": <mongo-id>,
//   "OtherApplications": [
//     {
//       "_id": <app-id>,
//       "ApplicationType": 29,
//       "CurrentStatus": 1,
//       "Title": "Search Term",
//       "ApplicationDate": ISODate("2026-01-15T10:30:00Z"),
//       "PaymentId": "RRR-ABC-123",
//       ...
//     }
//   ]
// }

// ============================================================
// 3. Get EVERYONE'S searches (what endpoint should return)
// ============================================================

db.appUsers.aggregate([
  { $unwind: "$OtherApplications" },
  { $match: { "OtherApplications.ApplicationType": 29 } },
  { $project: {
    userId: "$Id",
    search: "$OtherApplications"
  }}
]);

// Expected output: Multiple records, each showing one search from possibly different users
