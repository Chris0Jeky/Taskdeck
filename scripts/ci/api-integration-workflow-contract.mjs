function stepSection(workflowText, stepName) {
  const marker = `      - name: ${stepName}`
  const start = workflowText.indexOf(marker)
  if (start === -1) return null
  const next = workflowText.indexOf('\n      - name:', start + marker.length)
  return workflowText.slice(start, next === -1 ? workflowText.length : next)
}

function requireAlwaysNonFatal(errors, workflowText, stepName) {
  const section = stepSection(workflowText, stepName)
  if (section === null) {
    errors.push(`Missing API integration evidence step: ${stepName}`)
    return
  }
  if (!/^        if: always\(\)\s*$/m.test(section)) {
    errors.push(`${stepName} must run with if: always() after a timed-out test step`)
  }
  if (!/^        continue-on-error: true\s*$/m.test(section)) {
    errors.push(`${stepName} must be non-fatal so later timeout evidence is preserved`)
  }
}

function requireAlwaysConditionallyStrict(errors, workflowText, stepName, role) {
  const section = stepSection(workflowText, stepName)
  if (section === null) {
    errors.push(`Missing API integration evidence step: ${stepName}`)
    return null
  }
  if (!/^        if: always\(\)\s*$/m.test(section)) {
    errors.push(`${stepName} must run with if: always() after a timed-out test step`)
  }
  if (
    !section.includes(
      "continue-on-error: ${{ steps.api_integration_tests.outcome != 'success' }}",
    )
  ) {
    errors.push(
      `API integration ${role} must remain required after a successful test run and non-fatal after timeout`,
    )
  }
  return section
}

export function validateApiIntegrationWorkflow(
  workflowText,
  workflowPath = '.github/workflows/reusable-api-integration.yml',
) {
  const errors = []
  const testStep = stepSection(workflowText, 'Run API integration tests')

  if (testStep === null) {
    errors.push(`${workflowPath} is missing the API integration test step`)
  } else {
    if (!/^        timeout-minutes: 45\s*$/m.test(testStep)) {
      errors.push('API integration tests must retain the calibrated 45-minute timeout')
    }
    if (!testStep.includes('--blame-hang-timeout 20m')) {
      errors.push('API integration tests must retain the 20-minute per-test hang timeout')
    }
    if (!testStep.includes('--blame-hang-dump-type mini')) {
      errors.push('API integration tests must retain bounded mini hang dumps')
    }
    if (!testStep.includes('--diag "backend/TestResults/api-integration/${{ matrix.os }}/vstest-diagnostics.log"')) {
      errors.push('API integration tests must retain the VSTest diagnostic log')
    }
  }

  for (const stepName of [
    'Upload API integration test assembly diagnostics',
    'Finalize API integration runner context',
    'Upload API integration runner context',
  ]) {
    requireAlwaysNonFatal(errors, workflowText, stepName)
  }

  requireAlwaysConditionallyStrict(
    errors,
    workflowText,
    'Summarize API integration timing',
    'timing summarizer',
  )
  const timingUpload = requireAlwaysConditionallyStrict(
    errors,
    workflowText,
    'Upload API integration timing summary',
    'timing upload',
  )
  if (timingUpload !== null && !/^          if-no-files-found: error\s*$/m.test(timingUpload)) {
    errors.push('API integration timing upload must remain strict after a successful test run')
  }

  const failureEvidence = stepSection(workflowText, 'Upload API integration failure evidence')
  if (failureEvidence === null) {
    errors.push('Missing API integration failure evidence upload')
  } else {
    if (
      !failureEvidence.includes(
        "if: ${{ always() && steps.api_integration_tests.outcome != 'success' }}",
      )
    ) {
      errors.push('API integration failure evidence upload must run after a timed-out test step')
    }
    if (!/^        continue-on-error: true\s*$/m.test(failureEvidence)) {
      errors.push('API integration failure evidence upload must be non-fatal')
    }
    if (
      !failureEvidence.includes(
        'path: backend/TestResults/api-integration/${{ matrix.os }}/',
      )
    ) {
      errors.push('API integration failure evidence must upload the complete partial-results directory')
    }
    if (!/^          if-no-files-found: warn\s*$/m.test(failureEvidence)) {
      errors.push('API integration failure evidence must tolerate an empty timeout directory')
    }
  }

  return errors
}
