// ============================================================================
// demo/data.js — Fixed settings of the app. No sample data: devices,
// bookings, runs and test cases come from the backend once it is connected,
// and the store starts empty until then.
// ============================================================================

// The signed-in user. Placeholder until login is connected to the backend
// (POST /api/auth/login); "mine" (my booking, my run) compares with this name.
export const DEMO_USER = { name: 'Nguyen Minh Anh', role: 'TEST ENGINEER', initials: 'NA' };

// CAN lines in VDSA order and colors. The legend always lists all of them;
// the diagram draws only the lines the tool returned.
export const CAN_LINES = [
    { key: 'Info', color: '#3d6fe0' },
    { key: 'Body', color: '#f5a623' },
    { key: 'Chassis', color: '#0b1f8f' },
    { key: 'PT', color: '#e8171d' },
    { key: 'BA', color: '#a0302a' },
];

// Tools / agents on the bench PCs. Comes from the backend; none until then.
export const TOOLS = [];
