export interface NormalizedRegion { x: number; y: number; width: number; height: number }
export interface PixelRegion { left: number; top: number; width: number; height: number }

export function toPixels(region: NormalizedRegion, viewportWidth: number, viewportHeight: number): PixelRegion {
  const values = [region.x, region.y, region.width, region.height, viewportWidth, viewportHeight]
  if (values.some(value => !Number.isFinite(value)) || region.x < 0 || region.y < 0 || region.width <= 0 || region.height <= 0 || region.x + region.width > 1 || region.y + region.height > 1 || viewportWidth <= 0 || viewportHeight <= 0) {
    throw new Error('Invalid evidence region')
  }
  return {
    left: region.x * viewportWidth,
    top: region.y * viewportHeight,
    width: region.width * viewportWidth,
    height: region.height * viewportHeight,
  }
}
