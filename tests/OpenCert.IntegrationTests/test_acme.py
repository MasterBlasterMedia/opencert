"""End-to-end ACME checks with Pebble, real DNS-01 validation and local key material.
Run only against a dedicated Development API/worker using a disposable database.
No challenge-validation bypass is permitted in the Pebble configuration.
"""
import json, os, stat, subprocess, tempfile, time, unittest, uuid
from datetime import datetime
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
from urllib.request import Request, urlopen
from test_api import Client, enroll
if os.environ.get('OPENCERT_TEST_URL') != 'http://localhost:5081':
    raise RuntimeError('Start an isolated Pebble API on localhost:5081 and set OPENCERT_TEST_URL explicitly.')
DOTNET=os.environ.get('OPENCERT_DOTNET','dotnet')
AGENT=['apps/agent/bin/Debug/net10.0/OpenCert.Agent.dll']

def agent(*args):
    return subprocess.run([DOTNET,*AGENT,*map(str,args)],capture_output=True,text=True,timeout=20)
def wait_order(client,base,website,id,states):
    for _ in range(90):
        status,orders=client.request(base+'/websites/'+website+'/orders');assert status==200,(status,orders)
        order=next(o for o in orders if o['id']==id)
        if order['state'] in states:return order
        if order['state']=='Failed':raise AssertionError(order['diagnostic'])
        time.sleep(1)
    raise AssertionError('Worker did not reach '+str(states))
class AcmeJourney(unittest.TestCase):
    def test_dns_issuance_local_install_and_revocation(self):
        c=Client();_,me,_,_=c.login();enroll(c);base='/organizations/'+me['organizations'][0]['id']
        hostname='acme-'+uuid.uuid4().hex[:12]+'.example.com'
        status,site=c.request(base+'/websites','POST',{'hostname':hostname});self.assertEqual(status,201,site)
        with tempfile.TemporaryDirectory(prefix='opencert-acme-') as temporary:
            directory=Path(temporary)/'key';result=agent('csr',directory,hostname);self.assertEqual(result.returncode,0,result.stderr)
            self.assertEqual(stat.S_IMODE((directory/'private-key.pem').stat().st_mode),0o600)
            csr=(directory/'request.pem').read_text();key_before=(directory/'private-key.pem').read_bytes()
            def create_request(_):
                return c.request(base+'/websites/'+site['id']+'/orders','POST',{'csrPem':csr,'hostnames':[hostname],'acceptTerms':True})
            with ThreadPoolExecutor(max_workers=2) as pool: responses=list(pool.map(create_request,range(2)))
            self.assertEqual(sorted(status for status,_ in responses),[202,409],'concurrent requests must create only one active order')
            created=next(result for status,result in responses if status==202)
            order=wait_order(c,base,site['id'],created['id'],['AwaitingVerification'])
            challenges=json.loads(order['challengesJson']);self.assertGreater(len(challenges),0)
            for challenge in challenges:
                with urlopen(Request('http://localhost:8055/set-txt',data=json.dumps({'host':challenge['name']+'.','value':challenge['value']}).encode(),headers={'Content-Type':'application/json'}),timeout=5) as response:self.assertEqual(response.status,200)
            orderpath=base+'/orders/'+created['id']
            status,_=c.request(orderpath+'/verify','POST',{});self.assertEqual(status,202)
            order=wait_order(c,base,site['id'],created['id'],['Issued'])
            self.assertEqual(order['state'],'Issued');self.assertTrue(order['staging'])
            for _ in range(30):
                status,orders=c.request(base+'/websites/'+site['id']+'/orders');self.assertEqual(status,200)
                order=next(o for o in orders if o['id']==created['id'])
                if order['renewalInfoCheckedAt']:break
                time.sleep(1)
            self.assertEqual(order['renewalSource'],'Ari',order['renewalInfoError'])
            self.assertIsNone(order['renewalInfoError'])
            start,end,due,next_check,checked=[datetime.fromisoformat(order[field].replace('Z','+00:00')) for field in ['renewalWindowStart','renewalWindowEnd','renewalDueAt','nextRenewalInfoAt','renewalInfoCheckedAt']]
            self.assertLessEqual(start,due);self.assertLess(due,end);self.assertGreater(next_check,checked)
            status,pem=c.request(orderpath+'/certificate');self.assertEqual(status,200);self.assertIn('BEGIN CERTIFICATE',pem);self.assertNotIn('PRIVATE KEY',pem)
            outsider=Client();outsider.login();self.assertEqual(outsider.request(orderpath+'/certificate')[0],404)
            public=Path(temporary)/'chain.pem';public.write_text(pem)
            result=agent('install',directory,public);self.assertEqual(result.returncode,0,result.stderr)
            self.assertEqual((directory/'private-key.pem').read_bytes(),key_before,'installation never replaces the customer key')
            self.assertEqual((directory/'fullchain.pem').read_text(),pem)
            result=agent('inspect',public);self.assertEqual(result.returncode,0,result.stderr)
            self.assertEqual(agent('install',directory,public).returncode,0)
            self.assertEqual((directory/'previous-fullchain.pem').read_text(),pem,'replacement retains the prior public certificate')
            sentinel=Path(temporary)/'sentinel';sentinel.write_text('untouched')
            for filename in ['fullchain.pem','previous-fullchain.pem']:
                target=directory/filename;original=target.read_bytes();target.unlink();target.symlink_to(sentinel)
                self.assertNotEqual(agent('install',directory,public).returncode,0,'symlink target must be rejected')
                self.assertEqual(sentinel.read_text(),'untouched');target.unlink();target.write_bytes(original);target.chmod(0o600)
            link=Path(temporary)/'linked';link.symlink_to(directory,target_is_directory=True)
            self.assertNotEqual(agent('install',link,public).returncode,0,'linked key directories must be rejected')
            self.assertNotEqual(agent('csr',link/'nested',hostname).returncode,0,'linked parent must be rejected')
            self.assertFalse((directory/'nested').exists())
            for unsafe in [0o640,0o660,0o604]:
                (directory/'private-key.pem').chmod(unsafe)
                self.assertNotEqual(agent('install',directory,public).returncode,0,'shared private keys must be rejected')
            (directory/'private-key.pem').chmod(0o600)
            self.assertFalse(list(directory.glob('*.tmp')),'failed installs leave no temporary certificate files')
            other=Path(temporary)/'wrong-key';self.assertEqual(agent('csr',other,hostname).returncode,0)
            self.assertNotEqual(agent('install',other,public).returncode,0,'mismatched key must not install')
            self.assertNotEqual(c.request(base+'/websites/'+site['id'])[1]['status'],'Protected','issuance is not live HTTPS verification')
            self.assertEqual(c.request(orderpath+'/revoke','POST',{})[0],202)
            self.assertEqual(wait_order(c,base,site['id'],created['id'],['Revoked'])['state'],'Revoked')
            for challenge in challenges:
                with urlopen(Request('http://localhost:8055/clear-txt',data=json.dumps({'host':challenge['name']+'.'}).encode(),headers={'Content-Type':'application/json'}),timeout=5):pass
    def test_missing_dns_cannot_issue_a_certificate(self):
        c=Client();_,me,_,_=c.login();enroll(c);base='/organizations/'+me['organizations'][0]['id']
        hostname='unproved-'+uuid.uuid4().hex[:12]+'.example.com'
        status,site=c.request(base+'/websites','POST',{'hostname':hostname});self.assertEqual(status,201,site)
        with tempfile.TemporaryDirectory(prefix='opencert-acme-reject-') as temporary:
            directory=Path(temporary)/'key';self.assertEqual(agent('csr',directory,hostname).returncode,0)
            status,created=c.request(base+'/websites/'+site['id']+'/orders','POST',{'csrPem':(directory/'request.pem').read_text(),'hostnames':[hostname],'acceptTerms':True});self.assertEqual(status,202,created)
            wait_order(c,base,site['id'],created['id'],['AwaitingVerification'])
            orderpath=base+'/orders/'+created['id'];self.assertEqual(c.request(orderpath+'/verify','POST',{})[0],202)
            wait_order(c,base,site['id'],created['id'],['Failed'])
            self.assertEqual(c.request(orderpath+'/certificate')[0],409,'unproved domain must never receive a certificate')
if __name__=='__main__':unittest.main(verbosity=2)
