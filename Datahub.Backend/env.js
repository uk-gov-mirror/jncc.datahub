// expose our environment variables in one place
// so we're not just accessing process.env everywhere
module.exports = {
  get ES_ENDPOINT() { return process.env.ES_ENDPOINT },
  get ES_INDEX()    { return process.env.ES_INDEX },
  get AWS_PROFILE() { return process.env.AWS_PROFILE },
  get AWS_REGION()  { return process.env.AWS_REGION },
  get DATAHUB_ROOT() { return process.env.DATAHUB_ROOT },
}
