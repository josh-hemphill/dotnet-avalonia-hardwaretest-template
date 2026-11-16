// Shared assertions used by the CI runner tests.
import assert from "node:assert/strict";
export { default as assert } from "node:assert/strict";
export const assertEquals = assert.deepStrictEqual;
export const assertStringIncludes = (actual: string, expected: string) => assert.ok(actual.includes(expected), `${JSON.stringify(actual)} must include ${JSON.stringify(expected)}`);
export function assertThrows(fn: () => unknown, error: typeof Error, message: string): void {
  assert.throws(fn, (value: unknown) => value instanceof error && value.message.includes(message));
}
export const assertRejects = assert.rejects;
