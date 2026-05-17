export class StatusResetGuard {
  private generation = 0;

  nextGeneration(): number {
    this.generation += 1;
    return this.generation;
  }

  isCurrent(generation: number): boolean {
    return generation === this.generation;
  }
}
