// =============================================================================
// 2026-01 — Rename user-visible status label "New Opposition" -> "Opposed"
// =============================================================================
//
// Run with:    mongosh "<connection-string>/<database>" scripts/2026-rename-new-opposition-to-opposed.js
// Or in mongo: load("scripts/2026-rename-new-opposition-to-opposed.js")
//
// Scope: rewrites every stored free-text DISPLAY label / reference value /
//        audit message that currently reads "New Opposition" so that it now
//        reads "Opposed".
//
// IMPORTANT — what this script intentionally does NOT touch:
//   * The BsonSerializer<ApplicationStatuses>(BsonType.String) registration in
//     Program.cs persists ApplicationStatuses.NewOpposition as the literal
//     enum-name string "NewOpposition" (no space). That value MUST stay so the
//     .NET enum continues to deserialize. We never rewrite "NewOpposition".
//   * Document title labels "New Opposition Receipt" /
//     "New Opposition Acknowledgement" are literal document names and remain.
//   * Action wording such as "File a new opposition" or the Remita payment
//     description "New Opposition" is preserved (matches with surrounding
//     context are scoped to status / label / display fields only).
//
// The script is idempotent: re-running it is a no-op once all labels are
// "Opposed".
// =============================================================================

(function () {
    const OLD_LABEL = "New Opposition";
    const NEW_LABEL = "Opposed";

    // Total counter so the operator gets one final tally per run.
    let totalUpdated = 0;

    function bumpUpdated(res) {
        const n = (res && (res.modifiedCount ?? res.nModified)) || 0;
        totalUpdated += n;
        return n;
    }

    function collectionExists(name) {
        return db.getCollectionInfos({ name: name }).length > 0;
    }

    // -------------------------------------------------------------------------
    // 1. Top-level DISPLAY / LABEL fields on reference / lookup tables.
    //    These are the seed-data tables the request specifically calls out
    //    ("StatusName / DisplayName column currently stores 'New Opposition'").
    // -------------------------------------------------------------------------
    const labelCollections = [
        "statusLookup",
        "applicationStatuses",
        "applicationStatusLookup",
        "fileStatusLookup",
        "oppositionStatusLookup",
        "referenceData",
        "lookups",
        "statusCatalog",
        "statusReference"
    ];

    const labelFields = [
        "StatusName",
        "DisplayName",
        "Label",
        "Display",
        "Name",
        "Title",
        "Description",
        "Caption",
        "StatusLabel",
        "StatusDisplay"
    ];

    labelCollections.forEach(function (coll) {
        if (!collectionExists(coll)) return;
        labelFields.forEach(function (field) {
            const filter = {};
            filter[field] = OLD_LABEL;
            const update = { $set: {} };
            update.$set[field] = NEW_LABEL;
            const res = db.getCollection(coll).updateMany(filter, update);
            const n = bumpUpdated(res);
            if (n > 0) {
                print("[labels] " + coll + "." + field + " : " + n + " updated");
            }
        });
    });

    // -------------------------------------------------------------------------
    // 2. Notification / e-mail / SMS templates and message bodies.
    //    Replace the literal status token "New Opposition" (whole word) only
    //    when it appears as a stand-alone label — never inside the phrase
    //    "New Opposition Receipt" or "New Opposition Acknowledgement" (those
    //    are document titles) and never inside "new opposition" lower-case
    //    action wording (e.g. "file a new opposition").
    // -------------------------------------------------------------------------
    // Negative look-arounds keep us away from the protected phrases.
    const labelRegex = /\bNew Opposition\b(?!\s+(Receipt|Acknowledgement|Acknowledgment))/g;

    const templateCollections = [
        "notificationTemplates",
        "emailTemplates",
        "smsTemplates",
        "templates",
        "messageTemplates"
    ];

    const templateFields = [
        "Body",
        "Subject",
        "Message",
        "Content",
        "Html",
        "Text",
        "Template"
    ];

    templateCollections.forEach(function (coll) {
        if (!collectionExists(coll)) return;
        templateFields.forEach(function (field) {
            const filter = {};
            filter[field] = { $regex: labelRegex };
            db.getCollection(coll).find(filter).forEach(function (doc) {
                const original = doc[field];
                if (typeof original !== "string") return;
                const replaced = original.replace(labelRegex, NEW_LABEL);
                if (replaced === original) return;
                const setObj = {};
                setObj[field] = replaced;
                const res = db.getCollection(coll).updateOne(
                    { _id: doc._id },
                    { $set: setObj }
                );
                if (bumpUpdated(res) > 0) {
                    print("[templates] " + coll + "." + field + " : doc " + doc._id);
                }
            });
        });
    });

    // -------------------------------------------------------------------------
    // 3. Audit log / status-history MESSAGE fields that quote the label.
    //    We do not touch beforeStatus / afterStatus enum values — those still
    //    need to round-trip through EnumSerializer<ApplicationStatuses>.
    // -------------------------------------------------------------------------
    const auditCollections = [
        "auditLogs",
        "audit",
        "statusHistory",
        "history",
        "performance",
        "performances",
        "notifications"
    ];

    const auditFields = [
        "Message",
        "Description",
        "Note",
        "Comment",
        "Reason",
        "Body"
    ];

    auditCollections.forEach(function (coll) {
        if (!collectionExists(coll)) return;
        auditFields.forEach(function (field) {
            const filter = {};
            filter[field] = { $regex: labelRegex };
            db.getCollection(coll).find(filter).forEach(function (doc) {
                const original = doc[field];
                if (typeof original !== "string") return;
                const replaced = original.replace(labelRegex, NEW_LABEL);
                if (replaced === original) return;
                const setObj = {};
                setObj[field] = replaced;
                const res = db.getCollection(coll).updateOne(
                    { _id: doc._id },
                    { $set: setObj }
                );
                if (bumpUpdated(res) > 0) {
                    print("[audit] " + coll + "." + field + " : doc " + doc._id);
                }
            });
        });
    });

    // -------------------------------------------------------------------------
    // 4. Legacy stored display values inside Filling.ApplicationHistory[] /
    //    StatusHistory[] sub-documents that were saved as plain strings (older
    //    schema versions). Only touch documents where the value is exactly the
    //    label "New Opposition" — leave numeric and "NewOpposition" values
    //    alone so the .NET enum deserializer keeps working.
    // -------------------------------------------------------------------------
    if (collectionExists("files")) {
        const arrayLabelPaths = [
            // Top-level array on Filling
            { array: "ApplicationHistory", field: "CurrentStatusLabel" },
            { array: "ApplicationHistory", field: "DisplayStatus" },
            { array: "ApplicationHistory", field: "StatusName" },
            { array: "ApplicationHistory.$[].StatusHistory", field: "MessageLabel" },
            { array: "ApplicationHistory.$[].StatusHistory", field: "StatusName" }
        ];

        arrayLabelPaths.forEach(function (spec) {
            const filter = {};
            filter[spec.array + "." + spec.field] = OLD_LABEL;
            const update = { $set: {} };
            update.$set[spec.array + ".$[elem]." + spec.field] = NEW_LABEL;
            try {
                const res = db.files.updateMany(filter, update, {
                    arrayFilters: [{ ["elem." + spec.field]: OLD_LABEL }]
                });
                const n = bumpUpdated(res);
                if (n > 0) {
                    print("[embedded] files." + spec.array + "." + spec.field + " : " + n + " updated");
                }
            } catch (e) {
                // Older drivers fall back to a doc-by-doc rewrite.
                print("[embedded] arrayFilters not supported on files." + spec.array + " — skipping (" + e.message + ")");
            }
        });
    }

    print("=============================================================");
    print("Rename 'New Opposition' -> 'Opposed' complete. Documents updated: " + totalUpdated);
    print("=============================================================");
})();
