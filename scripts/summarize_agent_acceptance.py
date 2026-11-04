"""Publish measured acceptance facts without model prompts, raw responses or reasoning."""
import argparse
import collections
import json
from pathlib import Path


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('input',type=Path);p.add_argument('output',type=Path)
    a=p.parse_args();raw=json.loads(a.input.read_text());cases=[]
    for row in raw.get('cases',[]):
        answer=row.get('answer') or {};attempts=answer.get('Attempts',[]);steps=answer.get('Steps',[])
        cases.append({'case':row.get('case'),'request':row.get('prompt'),'controllerPassed':row.get('passed'),
                      'independentBehaviorVerificationPending':row.get('behaviorNotYetIndependentlyVerified',True),
                      'controllerChecks':row.get('checks'), 'status':answer.get('Status'),'isError':answer.get('IsError'),
                      'taskStatus':(answer.get('Task')or{}).get('Status'),
                      'inferenceCalls':len(attempts),'providerElapsedMs':sum(x.get('ProviderElapsedMs',0) for x in attempts),
                      'recoveryKinds':dict(collections.Counter(x['RepairKind'] for x in attempts if x.get('RepairKind'))),
                      'operations':dict(collections.Counter(x['Tool'] for x in steps)),
                      'operationResults':[{'index':i,'tool':x.get('Tool'),'success':x.get('Success'),'changesApplied':x.get('ChangesApplied')} for i,x in enumerate(steps)],
                      'reviewResults':[{'stage':r.get('ApproachStage'),'passed':r.get('Passed'),'atStep':r.get('StepCount')} for r in answer.get('EditorReviews',[])],
                      'finalReply':answer.get('Text')})
    result={k:raw.get(k) for k in ['startedUtc','finishedUtc','completed','passed','packageDigest','packageUnchanged','sceneRepairByHarness','providerInjected','paidApiUsed','stoppedByHarness','stopReason','continuationFromSavedArtifact','initialScene','packageSourceCommit','packageArchiveSha256','observerSnapshotIntervalSeconds','installedManifestFingerprintAdded','installedOriginalManifestFieldsUnchanged','installedPayloadMatchesFrozenProof','frozenSourceTreeSha256'] if k in raw}
    result.update({'rawModelInternalsOmitted':True,'cases':cases})
    if a.output.exists():raise FileExistsError('Preserve historical evidence: '+str(a.output))
    a.output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')


if __name__=='__main__':main()
