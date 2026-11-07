"""Black-box checks against a disposable Development API and real PostgreSQL.
Never run against a public deployment. No test backdoors or authentication bypasses.
"""
import base64, hashlib, hmac, http.cookiejar, json, os, re, struct, time, unittest, uuid
from pathlib import Path
from urllib.request import build_opener, HTTPCookieProcessor, Request
from urllib.error import HTTPError
BASE = os.environ.get('OPENCERT_TEST_URL', 'http://localhost:5080')
MAIL = Path(os.environ.get('OPENCERT_TEST_MAIL', '.local/mail'))
if not BASE.startswith(('http://localhost:', 'http://127.0.0.1:')): raise RuntimeError('Only a local disposable test API is allowed.')
class Client:
    def __init__(self): self.cookies=http.cookiejar.CookieJar(); self.opener=build_opener(HTTPCookieProcessor(self.cookies)); self.csrf=None
    def request(self,path,method='GET',body=None,csrf=True):
        if method not in ('GET','HEAD') and csrf and not self.csrf:
            status, value=self.request('/auth/csrf'); assert status==200,(status,value); self.csrf=value['token']
        headers={'Content-Type':'application/json'}
        if method!='GET' and csrf: headers['X-CSRF-Token']=self.csrf
        req=Request(BASE+'/api/v1'+path,data=json.dumps(body).encode() if body is not None else (b'' if method!='GET' else None),method=method,headers=headers)
        try:
            with self.opener.open(req,timeout=20) as response: status=response.status; data=response.read()
        except HTTPError as e: status=e.code; data=e.read(); e.close()
        try: value=json.loads(data) if data else None
        except ValueError: value=data.decode()
        if path.endswith('/finish') or path=='/auth/logout': self.csrf=None
        return status,value
    def begin(self,email):
        status,flow=self.request('/auth/email/start','POST',{'email':email}); assert status==202,(status,flow)
        for _ in range(30):
            for path in MAIL.glob('*.txt'):
                text=path.read_text()
                if 'flow='+flow['flowId'] in text: return flow['flowId'],re.search(r'code is (\d+)',text)[1]
            time.sleep(.1)
        raise AssertionError('Development email not delivered')
    def login(self):
        email='test-'+uuid.uuid4().hex+'@example.com'; flow,code=self.begin(email)
        status,result=self.request('/auth/email/finish','POST',{'flowId':flow,'code':code}); assert status==200,(status,result)
        status,me=self.request('/auth/me'); assert status==200,(status,me)
        return email,me,flow,code

def totp(secret):
    key=base64.b32decode(secret.upper()+'='*((8-len(secret)%8)%8)); digest=hmac.new(key,struct.pack('>Q',int(time.time())//30),hashlib.sha1).digest(); offset=digest[-1]&15
    return str((struct.unpack('>I',digest[offset:offset+4])[0]&0x7fffffff)%1000000).zfill(6)
def mail_matching(match):
    for _ in range(150):
        for path in MAIL.glob('*.txt'):
            text=path.read_text()
            if match(text): return text
        time.sleep(.2)
    raise AssertionError('Queued development email was not delivered')
def enroll(client):
    status,setup=client.request('/auth/totp/setup','POST',{}); assert status==200,(status,setup)
    status,result=client.request('/auth/totp/enable','POST',{'code':totp(setup['secret'])}); assert status==200,(status,result)
    return setup['secret'],result['recoveryCodes']
class ApiSecurityTests(unittest.TestCase):
    def test_complete_security_journey(self):
        a=Client(); email,me,flow,code=a.login(); org=me['organizations'][0]['id']; path='/organizations/'+org
        self.assertEqual(a.request('/auth/email/finish','POST',{'flowId':flow,'code':code})[0],400,'single-use email code')
        b=Client(); _,other,_,_=b.login()
        self.assertEqual(b.request(path+'/websites')[0],404,'cross-tenant list')
        self.assertEqual(a.request(path+'/websites','POST',{'hostname':'example.com'},csrf=False)[0],400,'CSRF enforced')
        self.assertEqual(a.request(path+'/websites','POST',{'hostname':'http://127.0.0.1'})[0],400,'unsafe hostname blocked')
        status,site=a.request(path+'/websites','POST',{'hostname':'HTTPS://EXAMPLE.COM/'}); self.assertEqual(status,201,site); self.assertEqual(site['hostname'],'example.com');self.assertEqual(site['status'],'Setup Required')
        sitepath=path+'/websites/'+site['id']; self.assertEqual(b.request(sitepath)[0],404)
        self.assertEqual(b.request(sitepath+'/check','POST',{})[0],404)
        self.assertEqual(a.request(sitepath+'/setup','PUT',{'method':'ProviderManaged'})[0],200)
        self.assertEqual(a.request(sitepath)[1]['setupMethod'],'ProviderManaged','onboarding persists')
        status,setup=a.request('/auth/totp/setup','POST',{});self.assertEqual(status,200,setup)
        status,result=a.request('/auth/totp/enable','POST',{'code':totp(setup['secret'])});self.assertEqual(status,200,result);recovery=result['recoveryCodes'][0]
        c=Client(); newflow,newcode=c.begin(email)
        self.assertEqual(Client().request('/auth/email/finish','POST',{'flowId':newflow,'code':newcode})[0],400,'browser binding')
        status,result=c.request('/auth/email/finish','POST',{'flowId':newflow,'code':newcode});self.assertEqual(status,200,result);self.assertTrue(result['requiresMfa'])
        self.assertEqual(c.request('/auth/me')[0],401,'email does not bypass MFA')
        self.assertEqual(c.request('/auth/mfa/finish','POST',{'flowId':newflow,'code':recovery})[0],204)
        self.assertEqual(c.request('/auth/mfa/finish','POST',{'flowId':newflow,'code':recovery})[0],400,'MFA challenge replay')
        status,sessions=c.request('/auth/sessions'); self.assertEqual(status,200,sessions)
        for session in sessions: self.assertEqual(c.request('/auth/sessions/'+session['id'],'DELETE')[0],204)
        self.assertEqual(c.request('/auth/me')[0],401,'server session revocation')
    def test_team_invitation_permissions_and_transfer(self):
        a=Client(); _,owner,_,_=a.login(); org=owner['organizations'][0]['id']; path='/organizations/'+org
        b=Client(); target,_,_,_=b.login()
        self.assertEqual(a.request(path+'/invitations','POST',{'email':target,'role':'Viewer'})[0],403,'strong authentication required')
        enroll(a)
        status,inv=a.request(path+'/invitations','POST',{'email':target,'role':'Viewer'});self.assertEqual(status,202,inv)
        text=mail_matching(lambda text: 'invite='+inv['id'] in text);token=re.search(r'token=([A-F0-9]+)',text)[1]
        accept='/invitations/'+inv['id']+'/accept'
        self.assertEqual(a.request(accept,'POST',{'token':token})[0],400,'invitation is email-bound')
        self.assertEqual(b.request(accept,'POST',{'token':token})[0],200)
        self.assertEqual(b.request(accept,'POST',{'token':token})[0],400,'invitation is single-use')
        self.assertEqual(b.request(path+'/websites','POST',{'hostname':'example.org'})[0],403,'viewer cannot write')
        status,members=a.request(path+'/members');self.assertEqual(status,200,members)
        targetid=next(m['userId'] for m in members if m['email'].lower()==target.lower());ownerid=next(m['userId'] for m in members if m['role']=='Owner')
        self.assertEqual(a.request(path+'/members/'+targetid+'/role','PUT',{'role':'Member'})[0],204)
        self.assertEqual(b.request(path+'/websites','POST',{'hostname':'example.org'})[0],201)
        self.assertEqual(a.request(path+'/members/'+ownerid,'DELETE')[0],409,'last owner protected')
        self.assertEqual(a.request(path+'/ownership','POST',{'userId':targetid})[0],204)
        self.assertEqual(a.request(path+'/members/'+targetid+'/role','PUT',{'role':'Viewer'})[0],403,'old owner loses ownership permissions immediately')
        self.assertEqual(a.request('/account/','DELETE',{'confirmation':'DELETE'})[0],204,'non-owner can delete personal account')
        self.assertEqual(a.request('/auth/me')[0],401)
    def test_email_change_recovery_rotation_and_mfa_removal(self):
        a=Client(); email,me,_,_=a.login();secret,oldcodes=enroll(a)
        status,result=a.request('/account/recovery-codes','POST',{});self.assertEqual(status,200,result);newcodes=result['recoveryCodes']
        b=Client();flow,code=b.begin(email);self.assertEqual(b.request('/auth/email/finish','POST',{'flowId':flow,'code':code})[0],200)
        self.assertEqual(b.request('/auth/mfa/finish','POST',{'flowId':flow,'code':oldcodes[0]})[0],400,'rotated recovery code is invalid')
        self.assertEqual(b.request('/auth/mfa/finish','POST',{'flowId':flow,'code':newcodes[0]})[0],204)
        fresh='changed-'+uuid.uuid4().hex+'@example.com';status,change=a.request('/account/email/start','POST',{'email':fresh});self.assertEqual(status,202,change)
        text=mail_matching(lambda t:fresh in t and 'email-change code is' in t);code=re.search(r'code is (\d+)',text)[1]
        self.assertEqual(b.request('/account/email/finish','POST',{'flowId':change['flowId'],'code':code})[0],400,'email change is session-bound')
        self.assertEqual(a.request('/account/email/finish','POST',{'flowId':change['flowId'],'code':code})[0],204)
        self.assertEqual(b.request('/auth/me')[0],401,'email change revokes other sessions')
        self.assertEqual(a.request('/auth/me')[1]['email'].lower(),fresh)
        self.assertEqual(a.request('/account/authenticator','DELETE',{'confirmation':'REMOVE'})[0],204)
        self.assertFalse(a.request('/auth/me')[1]['mfaEnabled'])
        self.assertEqual(a.request('/account/recovery-codes','POST',{})[0],403,'removal clears assurance')
        c=Client();flow,code=c.begin(fresh);status,result=c.request('/auth/email/finish','POST',{'flowId':flow,'code':code});self.assertEqual(status,200,result);self.assertFalse(result['requiresMfa'])
        enroll(a)
    def test_concurrent_recovery_code_consumption(self):
        from concurrent.futures import ThreadPoolExecutor
        a=Client();email,_,_,_=a.login();_,codes=enroll(a)
        clients=[Client(),Client()];flows=[]
        for c in clients:
            flow,code=c.begin(email);self.assertEqual(c.request('/auth/email/finish','POST',{'flowId':flow,'code':code})[0],200);flows.append(flow)
        with ThreadPoolExecutor(max_workers=2) as pool:
            result=list(pool.map(lambda item:item[0].request('/auth/mfa/finish','POST',{'flowId':item[1],'code':codes[0]})[0],zip(clients,flows)))
        self.assertEqual(sorted(result),[204,400],'one recovery code can create only one session')
    def test_passkey_options_require_csrf(self):
        c=Client();self.assertEqual(c.request('/auth/passkeys/login/options','POST',{},csrf=False)[0],400)
        status,value=c.request('/auth/passkeys/login/options','POST',{});self.assertEqual(status,200,value);self.assertEqual(value['userVerification'],'required');self.assertEqual(value['rpId'],'localhost')
if __name__=='__main__':unittest.main(verbosity=2)
