import base64
import os.path

def encode(value):
    return base64.b64encode(value.encode('utf-8')).decode('utf-8')

def decode(value):
    return base64.b64decode(value).decode('utf-8')
    
def get_octopusvariable(key):
    return octopusvariables[key]

def set_octopusvariable(name, value, sensitive=False):
    octopusvariables[name] = value
    name = encode(name)
    value = encode(value)

    if sensitive:
        print("##octopus[setVariable name='{0}' value='{1}' sensitive='{2}']".format(name, value, encode("True")))
    else:
        print("##octopus[setVariable name='{0}' value='{1}']".format(name, value))

def createartifact(path, fileName = None):
    if fileName is None:
        fileName = os.path.basename(path)

    serviceFileName = encode(fileName);

    length = str(os.stat(path).st_size()) if os.path.isfile(path) else "0"
    length = encode(length)

    path = os.path.abspath(path)
    servicepath = encode(path)

    print("##octopus[stdout-verbose]");
    print("Artifact {0} will be collected from {1} after this step completes".format(fileName, path))
    print("##octopus[stdout-default]");
    print("##octopus[createArtifact path='{0}' name='{1}' length='{2}']".format(servicepath, serviceFileName, length))

{{VariableDeclarations}}