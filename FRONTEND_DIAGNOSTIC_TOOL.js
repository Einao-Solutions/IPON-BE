// ============================================================
// FRONTEND DIAGNOSTIC TOOL
// Copy-paste this into browser Console to test
// ============================================================

(async function diagnose() {
  console.log("🔍 IPON Backend Diagnostics - Other Applications Endpoint\n");
  console.log("=" .repeat(60));

  const apiHost = prompt("Enter your API host (e.g., https://localhost:7001 or https://your-domain.com)");
  if (!apiHost) {
    alert("Cancelled");
    return;
  }

  const authToken = localStorage.getItem("authToken") || sessionStorage.getItem("authToken");
  if (!authToken) {
    console.error("❌ NO AUTH TOKEN FOUND");
    console.log("   - Ensure you're logged in");
    console.log("   - Check localStorage.authToken and sessionStorage.authToken");
    return;
  }

  console.log("\n✓ Auth token found");

  // Parse JWT to see claims
  try {
    const parts = authToken.split(".");
    if (parts.length === 3) {
      const payload = JSON.parse(atob(parts[1]));
      console.log("\n📋 JWT Claims:");
      console.log(`   User ID: ${payload["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"] || payload.sub || "MISSING"}`);
      console.log(`   Roles: ${payload["http://schemas.microsoft.com/ws/2008/06/identity/claims/role"] || payload.role || "MISSING"}`);
      console.log("\n   Full payload:", payload);
    }
  } catch (e) {
    console.warn("   (Could not parse JWT)");
  }

  // Test 1: Endpoint exists and responds
  console.log("\n" + "=".repeat(60));
  console.log("TEST 1: Basic endpoint connectivity");
  console.log("=".repeat(60));

  try {
    const testResponse = await fetch(
      `${apiHost}/api/users/GetOtherApplications`,
      {
        method: "GET",
        headers: {
          "Authorization": `Bearer ${authToken}`,
          "Content-Type": "application/json"
        }
      }
    );

    console.log(`Status: ${testResponse.status} ${testResponse.statusText}`);

    if (testResponse.status === 200) {
      const data = await testResponse.json();
      console.log(`✓ SUCCESS: Got ${data.length} applications`);

      if (data.length === 0) {
        console.warn("⚠️  Response is empty array. Possible reasons:");
        console.log("   1. No searches have been created and saved in the database");
        console.log("   2. Searches exist but in different database");
        console.log("   3. MongoDB query is filtering them out");
      } else {
        console.log("\n📊 Application Details:");
        data.slice(0, 3).forEach((app, i) => {
          console.log(`\n   App ${i + 1}:`);
          console.log(`     Title: ${app.title}`);
          console.log(`     Type: ${app.applicationType} (29=AvailabilitySearch)`);
          console.log(`     Status: ${app.currentStatus}`);
          console.log(`     Date: ${app.applicationDate}`);
          console.log(`     PaymentId: ${app.paymentId}`);
        });
        if (data.length > 3) {
          console.log(`   ... and ${data.length - 3} more`);
        }
      }
    } else if (testResponse.status === 403) {
      console.error("❌ 403 Forbidden");
      console.log("   Your account doesn't have permission.");
      console.log("   Required roles: Tech, TrademarkSupport, PatentDesignSupport, SuperAdmin");
    } else if (testResponse.status === 401) {
      console.error("❌ 401 Unauthorized");
      console.log("   Your auth token is invalid or expired.");
    } else {
      const errorText = await testResponse.text();
      console.error(`❌ ${testResponse.status}: ${errorText}`);
    }
  } catch (error) {
    console.error("❌ Network Error:", error.message);
    console.log("   Check:");
    console.log("   - API host is correct");
    console.log("   - Backend is running");
    console.log("   - CORS is enabled");
  }

  // Test 2: Try with explicit userId
  console.log("\n" + "=".repeat(60));
  console.log("TEST 2: With explicit userId parameter");
  console.log("=".repeat(60));

  try {
    const userId = prompt("Enter your user ID (optional, press Cancel to skip)");
    if (userId) {
      const response = await fetch(
        `${apiHost}/api/users/GetOtherApplications?userId=${encodeURIComponent(userId)}`,
        {
          method: "GET",
          headers: {
            "Authorization": `Bearer ${authToken}`,
            "Content-Type": "application/json"
          }
        }
      );

      console.log(`Status: ${response.status} ${response.statusText}`);
      const data = await response.json();
      console.log(`Got ${data.length} applications for ${userId}`);
      console.log("Data:", data);
    }
  } catch (error) {
    console.error("Error:", error);
  }

  console.log("\n" + "=".repeat(60));
  console.log("✅ Diagnostic complete");
  console.log("=".repeat(60));
})();
