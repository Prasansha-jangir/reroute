import js from '@eslint/js';
import globals from 'globals';
import reactHooks from 'eslint-plugin-react-hooks';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  { ignores: ['dist', 'coverage', 'playwright-report', 'public', 'src/shared/api/generated'] },
  {
    extends: [js.configs.recommended, ...tseslint.configs.strict],
    files: ['**/*.{ts,tsx}'],
    languageOptions: { ecmaVersion: 2022, globals: globals.browser },
    plugins: { 'react-hooks': reactHooks },
    rules: {
      ...reactHooks.configs.recommended.rules,
      // Feature isolation: features may use shared/, but never reach into another feature's internals.
      'no-restricted-imports': ['error', { patterns: [{ group: ['@/features/*/*'], message: 'Import from the feature public API (index.ts) instead.' }] }],
    },
  },
);
