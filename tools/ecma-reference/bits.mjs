// Exact double <-> hex bit pattern conversion, so JSON carries -0, NaN and every last digit.
const view = new DataView(new ArrayBuffer(8));

export function toBits(x) {
  view.setFloat64(0, x);
  return "0x" + view.getBigUint64(0).toString(16).padStart(16, "0");
}

export function fromBits(hex) {
  view.setBigUint64(0, BigInt(hex));
  return view.getFloat64(0);
}
