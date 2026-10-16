import hmac
import hashlib
import json
import urllib.request

CHECKSUM_KEY = "618cc06cdcdcc939b290d7f8730b5c65bae420ef310fa049f735f1609efae110"
ORDER_CODE = 3
AMOUNT = 190000
TXN_REF = "2aef04e9130e41eda6f29e9c8fc8477a"

data = {
    "accountNumber": "12345678",
    "amount": AMOUNT,
    "code": "00",
    "currency": "VND",
    "description": "ExtraEfficiency DH-20261016-0003",
    "orderCode": ORDER_CODE,
    "paymentLinkId": TXN_REF,
    "reference": "REF-3",
    "transactionDateTime": "2026-10-16 15:40:50"
}

# PayOS ConvertToSortedQueryString sorts by key name alphabetically:
sorted_pairs = []
for k in sorted(data.keys()):
    v = data[k]
    if v is not None and str(v) != "":
        sorted_pairs.append(f"{k}={v}")

query_string = "&".join(sorted_pairs)
print("Data query string:", query_string)

signature = hmac.new(CHECKSUM_KEY.encode("utf-8"), query_string.encode("utf-8"), hashlib.sha256).hexdigest().lower()
print("Signature:", signature)

payload = {
    "code": "00",
    "desc": "success",
    "success": True,
    "data": data,
    "signature": signature
}

req_data = json.dumps(payload).encode("utf-8")
req = urllib.request.Request(
    "http://localhost:5001/api/payments/webhook",
    data=req_data,
    headers={"Content-Type": "application/json", "x-signature": signature}
)

try:
    with urllib.request.urlopen(req) as resp:
        print("Status:", resp.status)
        print("Response:", resp.read().decode("utf-8"))
except urllib.error.HTTPError as e:
    print("HTTP Error:", e.code)
    print("Body:", e.read().decode("utf-8"))
