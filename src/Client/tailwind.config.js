/** @type {import('tailwindcss').Config} */
module.exports = {
    content: [
        "./index.html",
        "./**/*.{fs,js,ts,jsx,tsx}",
        "!./node_modules/**/*",
    ],
    theme: {
        extend: {},
    },
    plugins: [require("daisyui")],
    daisyui: {
        themes: ["light", "dark", "corporate", "night"],
        darkTheme: "dark",
    },
}
