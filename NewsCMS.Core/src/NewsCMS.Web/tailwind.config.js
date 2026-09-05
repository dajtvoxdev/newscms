module.exports = {
  content: [
    './Areas/Admin/**/*.cshtml',
    './Areas/Admin/**/*.cshtml.cs',
    './Pages/**/*.cshtml',
    './Views/**/*.cshtml'
  ],
  theme: {
    extend: {
      fontFamily: {
        sans: ['Inter', 'ui-sans-serif', 'system-ui', 'sans-serif']
      },
      boxShadow: {
        soft: '0 18px 60px rgba(15, 23, 42, 0.08)'
      }
    }
  }
};
